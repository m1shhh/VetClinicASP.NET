using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using VetClinic.Services;
using VetClinicASP.NET.Infrastructure;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Криптиране при запис (Task 6, design doc §3.2/§3.3): 10-те криптирани колони държат
    /// "enc:v1:..." в БАЗАТА (проверка през сурова SqlClient връзка — ValueConverter-ът
    /// не участва в проверката), Patient.Name остава cleartext, runner-ът е idempotentен
    /// (второ пускане = byte-идентичен шифротекст), а търсенето по Owner.FullName работи
    /// след криптирането (in-memory клонът в PatientService — SQL LIKE върху шифротекст
    /// би бил тихо грешен).
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class EncryptionAtRestTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public EncryptionAtRestTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void EncryptExistingData_EncryptsOwnerColumnsAtRest_AndPatientNameStaysPlaintext()
        {
            var (ownerId, _) = SeedOwnerAndPatientRaw();

            // Преди runner-а: сурово прочетената стойност Е plaintext — доказателство, че
            // seed-ът мина през RAW SQL, а не през конвертор-контекст.
            var before = ReadOwnerRowRaw(ownerId);
            Assert.StartsWith("Иван Иванов", before.FullName);
            Assert.False(before.FullName.StartsWith("enc:v1:", StringComparison.Ordinal));

            RunEncryptExisting();

            var after = ReadOwnerRowRaw(ownerId);

            // Всичките 4 криптирани колони на Owner започват с "enc:v1:" В БАЗАТА.
            Assert.StartsWith("enc:v1:", after.FullName);
            Assert.StartsWith("enc:v1:", after.PhoneNumber);
            Assert.StartsWith("enc:v1:", after.Email);
            Assert.StartsWith("enc:v1:", after.Address);

            // Patient.Name НЕ е криптиран (cleartext за търсене/показване — design doc §4).
            Assert.Equal("Мурзик", after.PatientName);
            Assert.False(after.PatientName.StartsWith("enc:v1:", StringComparison.Ordinal));

            // Кръгъл трип с СЪЩИЯ тестов ключ (fixture.Encryption — ключът на конверторите).
            Assert.Equal("Иван Иванов", _fixture.Encryption.Decrypt(after.FullName));
        }

        [Fact]
        public void EncryptExistingData_IsIdempotent_SecondRunKeepsCiphertextByteIdentical()
        {
            var (ownerId, _) = SeedOwnerAndPatientRaw();

            RunEncryptExisting();
            var firstRun = ReadOwnerRowRaw(ownerId);

            Assert.StartsWith("enc:v1:", firstRun.FullName); // sanity: първото пускане криптира

            // Второ пускане: префиксът вече е наличен → write конверторът пропуска as-is →
            // EF не открива промяна → няма UPDATE → стойностите byte-идентични.
            RunEncryptExisting();
            var secondRun = ReadOwnerRowRaw(ownerId);

            Assert.Equal(firstRun.FullName, secondRun.FullName);
            Assert.Equal(firstRun.PhoneNumber, secondRun.PhoneNumber);
            Assert.Equal(firstRun.Email, secondRun.Email);
            Assert.Equal(firstRun.Address, secondRun.Address);
        }

        [Fact]
        public void GetAllPatients_ByOwnerSearchString_FindsPatientOverEncryptedColumns()
        {
            var (ownerId, patientId) = SeedOwnerAndPatientRaw();

            RunEncryptExisting(); // колоните вече са шифротекст в базата

            using var context = _fixture.CreateContext(new TestClinicContext(1));
            var service = new PatientService(context, new TestClinicContext(1));

            // SQL LIKE върху шифротекст не би намерил нищо — клонът "owner" в PatientService
            // материализира и търси В ПАМЕТА върху декриптираните стойности.
            var patients = service
                .GetAllPatients("иван иванов", "owner", "asc")
                .ToList();

            Assert.Contains(patients, p => p.PatientId == patientId);
            var found = patients.Single(p => p.PatientId == patientId);
            Assert.Equal(ownerId, found.OwnerId);
            // Декриптирането при материализация е прозрачно: името на стопанина е plaintext.
            Assert.Equal("Иван Иванов", found.Owner!.FullName);

            // GetOwnersList (проекция на x.FullName в KeyValuePair) — декриптира при
            // материализация; SQL-странно не се прави низови операции върху колоната.
            var owners = service.GetOwnersList();
            Assert.Contains(owners, pair => pair.Value == "Иван Иванов");
        }

        private void RunEncryptExisting()
        {
            // TestClinicContext(null) е достатъчен — runner-ът е maintenance инструмент
            // (IgnoreQueryFilters) и вижда всички клиники независимо от ICurrentClinic.
            using var context = _fixture.CreateContext(new TestClinicContext(null));
            var runner = new EncryptExistingDataService(
                context,
                _fixture.Encryption,
                NullLogger<EncryptExistingDataService>.Instance);

            runner.Run();
        }

        /// <summary>
        /// Seed през RAW SQL (Microsoft.Data.SqlClient): запис през конвертор-контекст би
        /// криптирал още при записа и тестът нямаше да покрива legacy plaintext пътя.
        /// ClinicID=1 е пилотната клиника от миграцията (AddClinicAndClinicId).
        /// </summary>
        private (int OwnerId, int PatientId) SeedOwnerAndPatientRaw()
        {
            using var connection = new SqlConnection(_fixture.ConnectionString);
            connection.Open();

            int ownerId;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO Owners (FullName, PhoneNumber, Email, Address, ClinicID)
OUTPUT INSERTED.OwnerID
VALUES (N'Иван Иванов', N'0888 123 456', N'ivan.ivanov@example.bg', N'ул. Витоша 15, София', 1);";

                ownerId = (int)command.ExecuteScalar()!;
            }

            int patientId;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO Patients (Name, Species, OwnerID, ClinicID)
OUTPUT INSERTED.PatientID
VALUES (N'Мурзик', N'Котка', @ownerId, 1);";

                var parameter = command.Parameters.Add("@ownerId", System.Data.SqlDbType.Int);
                parameter.Value = ownerId;

                patientId = (int)command.ExecuteScalar()!;
            }

            return (ownerId, patientId);
        }

        private (string FullName, string PhoneNumber, string Email, string Address, string PatientName)
            ReadOwnerRowRaw(int ownerId)
        {
            using var connection = new SqlConnection(_fixture.ConnectionString);
            connection.Open();

            string fullName, phoneNumber, email, address, patientName;

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT FullName, PhoneNumber, Email, Address FROM Owners WHERE OwnerID = @ownerId;";

                var parameter = command.Parameters.Add("@ownerId", System.Data.SqlDbType.Int);
                parameter.Value = ownerId;

                using var reader = command.ExecuteReader();
                Assert.True(reader.Read(), $"Owner {ownerId} липсва.");
                fullName = reader.GetString(0);
                phoneNumber = reader.GetString(1);
                email = reader.GetString(2);
                address = reader.GetString(3);
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Name FROM Patients WHERE OwnerID = @ownerId;";

                var parameter = command.Parameters.Add("@ownerId", System.Data.SqlDbType.Int);
                parameter.Value = ownerId;

                using var reader = command.ExecuteReader();
                Assert.True(reader.Read(), $"Пациент на Owner {ownerId} липсва.");
                patientName = reader.GetString(0);
            }

            return (fullName, phoneNumber, email, address, patientName);
        }
    }
}
