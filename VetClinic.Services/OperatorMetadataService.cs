using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    /// <summary>
    /// Метаданен сервис за операторския панел (Task 9, design doc §3.3/§8).
    /// Операторът е null-clinic потребител — затова ВСИЧКИ заявки тук са IgnoreQueryFilters()
    /// и връщат САМО агрегати (Count по ClinicId) + Clinic-метаданни. Съдържанието
    /// (пациенти/стопани/записи) НИКОГА не минава през този клас — интерфейсът го забранява.
    /// Clinic НЕ е query-филтриран (tenant коренът), но IgnoreQueryFilters е изричен за
    /// яснота: операторът вижда всички клиники.
    ///
    /// UserManager-ът и VetClinicContext-ът са СЪЩИЯТ scoped instance (DI) — затова EF
    /// транзакцията в CreatePilotClinic покрива Clinic + user + роля ЕДНОВРЕМЕННО
    /// (същият механизъм като StaffService.CreateStaff / AccountController.Register).
    /// ICurrentClinic се инжектира за изричност: сервисът не го ползва за данни —
    /// операторският контекст няма clinic_id, а метаданните не зависят от него.
    /// </summary>
    public class OperatorMetadataService : IOperatorMetadataService
    {
        private readonly VetClinicContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICurrentClinic _currentClinic;

        public OperatorMetadataService(
            VetClinicContext context,
            UserManager<ApplicationUser> userManager,
            ICurrentClinic currentClinic)
        {
            _context = context;
            _userManager = userManager;
            _currentClinic = currentClinic;
        }

        public List<OperatorClinicRow> GetClinics()
        {
            // Само АГРЕГАТИ — GroupBy Count по ClinicId, без никакви tenant колони/редове.
            var patientCounts = _context.Patients
                .IgnoreQueryFilters()
                .GroupBy(p => p.ClinicId)
                .Select(g => new { ClinicId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.ClinicId, x => x.Count);

            var appointmentCounts = _context.Appointments
                .IgnoreQueryFilters()
                .GroupBy(a => a.ClinicId)
                .Select(g => new { ClinicId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.ClinicId, x => x.Count);

            // Clinic-метаданните (име/статус/дата/IsActive) — операторът ги вижда по дизайн.
            var clinics = _context.Clinics
                .IgnoreQueryFilters()
                .OrderBy(c => c.ClinicId)
                .Select(c => new { c.ClinicId, c.Name, c.BillingStatus, c.CreatedAtUtc, c.IsActive })
                .ToList();

            return clinics
                .Select(c => new OperatorClinicRow(
                    c.ClinicId,
                    c.Name,
                    c.BillingStatus.ToString(),
                    c.CreatedAtUtc,
                    c.IsActive,
                    patientCounts.GetValueOrDefault(c.ClinicId),
                    appointmentCounts.GetValueOrDefault(c.ClinicId)))
                .ToList();
        }

        public void SetActive(int clinicId, bool active)
        {
            // Clinic НЕ е query-филтриран — директно по id, независимо от ICurrentClinic.
            var clinic = _context.Clinics
                .FirstOrDefault(c => c.ClinicId == clinicId);

            if (clinic == null)
            {
                throw new InvalidOperationException("Клиниката не е намерена.");
            }

            clinic.IsActive = active;
            _context.SaveChanges();
        }

        public int CreatePilotClinic(string name, string adminEmail, string adminDisplayName, string adminPassword)
        {
            // Атомарност (план, Task 9): Clinic + ApplicationUser + роля — ЕДНА транзакция върху
            // споделения context (UserManager минава през същия EF store). При провал — rollback,
            // без "сираче" Clinic ред или акаунт без роля.
            using var transaction = _context.Database.BeginTransaction();

            var clinic = new Clinic
            {
                Name = name,
                IsActive = true,
                BillingStatus = ClinicBillingStatus.Active,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.Clinics.Add(clinic);
            _context.SaveChanges(); // flush → identity ClinicId

            var user = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = adminDisplayName,
                ClinicId = clinic.ClinicId,
                IsActive = true
            };

            var createResult = _userManager.CreateAsync(user, adminPassword).GetAwaiter().GetResult();
            if (!createResult.Succeeded)
            {
                transaction.Rollback();
                _context.ChangeTracker.Clear();
                throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }

            // Резултатът от AddToRoleAsync НЕ се пренебрегва (урок от Task 7 ревю):
            // провал → rollback — пилотната клиника не остава без ClinicAdmin.
            var roleResult = _userManager.AddToRoleAsync(user, AppRoles.ClinicAdmin).GetAwaiter().GetResult();
            if (!roleResult.Succeeded)
            {
                transaction.Rollback();
                _context.ChangeTracker.Clear();
                throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }

            transaction.Commit();
            return clinic.ClinicId;
        }
    }
}
