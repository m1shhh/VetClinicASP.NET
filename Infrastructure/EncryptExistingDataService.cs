using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinicASP.NET.Infrastructure
{
    /// <summary>
    /// Однократен поддръжков инструмент (`dotnet run -- --encrypt-existing`): криптира
    /// вече-съществуващите plaintext стойности в 10-те криптирани колони (design doc §3.2/§3.3).
    ///
    /// Механика (idempotentна):
    /// 1. Суров ADO преглед върху връзката на контекста установява кои клетки ОЩЕ нямат
    ///    префикса "enc:v1:" (legacy plaintext) — ValueConverter-ът не участва в тази проверка.
    /// 2. Зареждане на редовете с <see cref="QueryableExtensions.IgnoreQueryFilters{TEntity}"/> —
    ///    maintenance инструментът вижда ВСИЧКИ клиники (ICurrentClinic е null-clinic в
    ///    console режим → query filter-ите иначе deny-ват всичко).
    /// 3. За legacy клетки: стойността се презаписва с <see cref="EncryptionService.Encrypt"/> —
    ///    EF открива промяна (криптирано ≠ оригиналния plaintext snapshot) → UPDATE, при който
    ///    write конверторът пропуска вече-префиксираната стойност as-is. Така се криптира
    ///    РОВНО веднъж.
    /// 4. Второ пускане: прегледът не намира префикс-липсващи клетки → никакви промени →
    ///    EF не генерира UPDATE → шифротекстът остава byte-идентичен.
    /// </summary>
    public class EncryptExistingDataService
    {
        private const string TextPrefix = "enc:v1:";

        private readonly VetClinicContext _context;
        private readonly EncryptionService _encryption;
        private readonly ILogger<EncryptExistingDataService> _logger;

        public EncryptExistingDataService(
            VetClinicContext context,
            EncryptionService encryption,
            ILogger<EncryptExistingDataService> logger)
        {
            _context = context;
            _encryption = encryption;
            _logger = logger;
        }

        public void Run()
        {
            _logger.LogInformation(
                "EncryptExistingData: старт (idempotentно — вече криптираните редове не се пипат).");

            var written = new List<(string Table, int Rows)>
            {
                ("Owners", EncryptOwners()),
                ("Appointments", EncryptAppointments()),
                ("MedicalRecords", EncryptMedicalRecords()),
                ("Veterinarians", EncryptVeterinarians()),
            };

            // Patients и Vaccinations нямат криптирани колони (Patient.Name/Breed и
            // VaccineName остават cleartext за търсене/сортиране — design doc §4) —
            // нищо за мигриране; изброяваме ги за пълнота на шестте tenant таблици.
            written.Add(("Patients", 0));
            written.Add(("Vaccinations", 0));

            foreach (var (table, rows) in written)
                _logger.LogInformation("EncryptExistingData: {Table} — записани {Rows} реда.", table, rows);

            _logger.LogInformation("EncryptExistingData: край.");
        }

        private int EncryptOwners()
        {
            var legacy = ScanLegacyRaw("Owners", "OwnerID", "FullName", "PhoneNumber", "Email", "Address");

            var rows = _context.Owners
                .IgnoreQueryFilters() // maintenance: всички клиники
                .ToList();

            return EncryptAndSave(rows, o => o.OwnerId, legacy,
                "FullName", "PhoneNumber", "Email", "Address");
        }

        private int EncryptAppointments()
        {
            var legacy = ScanLegacyRaw("Appointments", "AppointmentID", "Reason");

            var rows = _context.Appointments
                .IgnoreQueryFilters()
                .ToList();

            return EncryptAndSave(rows, a => a.AppointmentId, legacy, "Reason");
        }

        private int EncryptMedicalRecords()
        {
            var legacy = ScanLegacyRaw("MedicalRecords", "RecordID", "Diagnosis", "Treatment", "Notes");

            var rows = _context.MedicalRecords
                .IgnoreQueryFilters()
                .ToList();

            return EncryptAndSave(rows, r => r.RecordId, legacy,
                "Diagnosis", "Treatment", "Notes");
        }

        private int EncryptVeterinarians()
        {
            var legacy = ScanLegacyRaw("Veterinarians", "VetID", "PhoneNumber", "Email");

            var rows = _context.Veterinarians
                .IgnoreQueryFilters()
                .ToList();

            return EncryptAndSave(rows, v => v.VetId, legacy, "PhoneNumber", "Email");
        }

        /// <summary>
        /// Суров SELECT (без конвертора) → за всяка колона: id-та на редове, чиито стойности
        /// нямат "enc:v1:" префикс (legacy plaintext за криптиране). NULL стойности се пропускат.
        /// Използва връзката на контекста; имена на таблици/колони са константи в кода.
        /// </summary>
        private Dictionary<string, HashSet<int>> ScanLegacyRaw(
            string table, string keyColumn, params string[] columns)
        {
            var connection = (Microsoft.Data.SqlClient.SqlConnection)_context.Database.GetDbConnection();

            var ownedConnection = false;
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
                ownedConnection = true;
            }

            try
            {
                var legacy = columns.ToDictionary(c => c, _ => new HashSet<int>());

                using var command = connection.CreateCommand();
                command.CommandText =
                    $"SELECT [{keyColumn}], {string.Join(", ", columns.Select(c => $"[{c}]"))} FROM [{table}]";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);

                    for (var i = 0; i < columns.Length; i++)
                    {
                        if (reader.IsDBNull(i + 1))
                            continue;

                        var raw = reader.GetString(i + 1);
                        if (!raw.StartsWith(TextPrefix, StringComparison.Ordinal))
                            legacy[columns[i]].Add(id);
                    }
                }

                return legacy;
            }
            finally
            {
                if (ownedConnection)
                    connection.Close();
            }
        }

        /// <summary>
        /// Презаписва legacy клетките с новокриптирана стойност (през EncryptionService —
        /// write конверторът после я пропуска as-is), записва и връща броя реда с реален
        /// UPDATE. Вече-криптирани клетки НЕ се пипат (idempotence).
        /// </summary>
        private int EncryptAndSave<TEntity>(
            IReadOnlyList<TEntity> rows,
            Func<TEntity, int> key,
            IReadOnlyDictionary<string, HashSet<int>> legacy,
            params string[] properties)
        {
            var touched = new HashSet<int>();

            foreach (var row in rows)
            {
                var id = key(row);
                if (!properties.Any(p => legacy[p].Contains(id)))
                    continue;

                var entry = _context.Entry(row!);

                foreach (var property in properties)
                {
                    if (!legacy[property].Contains(id))
                        continue;

                    var current = (string?)entry.Property(property).CurrentValue;
                    if (current is null || current.StartsWith(TextPrefix, StringComparison.Ordinal))
                        continue;

                    entry.Property(property).CurrentValue = _encryption.Encrypt(current);
                    touched.Add(id);
                }
            }

            _context.SaveChanges();
            return touched.Count;
        }
    }
}
