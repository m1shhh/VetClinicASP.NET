using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    /// <summary>
    /// GDPR erasure fan-out (Task 11, design doc §7).
    ///
    /// РЕШЕНИЕ за одита (разширение от Task 10 ревю): `clinic.deleted` се записва В
    /// ТРАНЗАКЦИЯТА на изтриването, директно през <see cref="VetClinicContext.AuditLogs"/> —
    /// съзнателно изключение от правилото "IAuditService е единственият записвач". Причина:
    /// destructive операция трябва да е FAIL-CLOSED — ако одитният ред не може да се запази,
    /// цялото изтриване се връща назад. IAuditService.Log е fail-open за бизнес операциите
    /// (Task 11, Part 5) и не може да даде тази гаранция. Няма PII: само код, тип, Id-та и
    /// actor UserId.
    ///
    /// Редът на изтриване е dict-ван от FK-тата: децата преди родителите, потребителите
    /// преди Clinic реда (FK_AspNetUsers_Clinic е Restrict). Всички заявки са с
    /// IgnoreQueryFilters() + изричен ClinicId филтър — erasure работи и извън текущия
    /// tenant контекст, но НЕ пипа други клиники.
    /// </summary>
    public class ErasureService : IErasureService
    {
        private const string ClinicDeletedAction = "clinic.deleted";
        private const string ClinicEntity = "Clinic";

        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<ErasureService> _logger;

        public ErasureService(
            VetClinicContext context,
            ICurrentClinic currentClinic,
            IHttpContextAccessor httpContextAccessor,
            ILogger<ErasureService> logger)
        {
            _context = context;
            _currentClinic = currentClinic;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public void EraseClinic(int clinicId)
        {
            using var transaction = _context.Database.BeginTransaction();

            // Clinic НЕ е tenant-филтриран — проверката е директна. Липсваща клиника =
            // no-op (идемпотентност): без изключение, без одитен ред, без частични промени.
            var clinic = _context.Clinics.FirstOrDefault(c => c.ClinicId == clinicId);
            if (clinic == null)
            {
                _logger.LogInformation(
                    "Erasure no-op: clinic {ClinicId} does not exist (already erased or unknown).",
                    clinicId);
                return;
            }

            // 1. Децата на пациентите (FK-та сочат към Patients/Veterinarians).
            _context.Vaccinations
                .IgnoreQueryFilters()
                .Where(e => e.ClinicId == clinicId)
                .ExecuteDelete();

            _context.MedicalRecords
                .IgnoreQueryFilters()
                .Where(e => e.ClinicId == clinicId)
                .ExecuteDelete();

            _context.Appointments
                .IgnoreQueryFilters()
                .Where(e => e.ClinicId == clinicId)
                .ExecuteDelete();

            // 2. Пациенти → стопани.
            _context.Patients
                .IgnoreQueryFilters()
                .Where(e => e.ClinicId == clinicId)
                .ExecuteDelete();

            _context.Owners
                .IgnoreQueryFilters()
                .Where(e => e.ClinicId == clinicId)
                .ExecuteDelete();

            // 3. Ветеринарни профили (вече без референции от записи/ваксинации/часове).
            _context.Veterinarians
                .IgnoreQueryFilters()
                .Where(e => e.ClinicId == clinicId)
                .ExecuteDelete();

            // 4. Потребителите на клиниката + Identity join редовете им. AspNetUsers няма
            // query filter; филтърът по ClinicId е ИЗРИЧЕН. Списъкът се материализира
            // ПРЕДИ изтриването, за да не остане празна subquery за join редовете.
            var userIds = _context.Users
                .Where(u => u.ClinicId == clinicId)
                .Select(u => u.Id)
                .ToList();

            if (userIds.Count > 0)
            {
                _context.UserRoles.Where(r => userIds.Contains(r.UserId)).ExecuteDelete();
                _context.UserClaims.Where(c => userIds.Contains(c.UserId)).ExecuteDelete();
                _context.UserLogins.Where(l => userIds.Contains(l.UserId)).ExecuteDelete();
                _context.UserTokens.Where(t => userIds.Contains(t.UserId)).ExecuteDelete();
            }

            _context.Users
                .Where(u => u.ClinicId == clinicId)
                .ExecuteDelete();

            // 5. Одит В ТРАНЗАКЦИЯТА (fail-closed): ако този SaveChanges гръмне, целият
            // fan-out се връща назад. ClinicId остава в одита, за да е проследимо КОЯ
            // клиника е изтрита, без да пази нейно съдържание.
            var actorUserId = _httpContextAccessor.HttpContext?.User
                ?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            _context.AuditLogs.Add(new AuditLog
            {
                ClinicId = clinicId,
                UserId = actorUserId,
                Action = ClinicDeletedAction,
                Entity = ClinicEntity,
                EntityId = clinicId,
                AtUtc = DateTime.UtcNow
            });

            // 6. Clinic редът последен (всички Restrict FK-та вече са без референции).
            _context.Clinics.Remove(clinic);

            try
            {
                _context.SaveChanges();
                transaction.Commit();
            }
            catch (Exception ex)
            {
                // Fail-closed: логваме БЕЗ PII (само ClinicId) и оставяме изключението да
                // стигне до контролера; using-dispose на транзакцията я връща назад.
                _logger.LogError(
                    ex,
                    "Erasure failed for clinic {ClinicId}; the transaction was rolled back.",
                    clinicId);
                throw;
            }

            _logger.LogInformation(
                "Clinic {ClinicId} erased with all tenant data ({UserCount} user account(s); " +
                "actor clinic context: {ActorClinicId}).",
                clinicId,
                userIds.Count,
                _currentClinic.ClinicId);
        }
    }
}
