namespace VetClinicASP.NET.Models;

/// <summary>
/// Одитен запис (Task 10, design doc §4/§7): САМО кодове и Id-та — ЗАБРАНЕНО е PII
/// (имена, имейли, пароли) и IP адреси.
///
/// НЕ е tenant entity: ClinicId е nullable (null за операторски действия) и НЯМА query
/// filter — IAuditService е единственият записвач, а четенето (Phase 2, ако изобщо) е
/// операторско. Без FK-та по замисъл:
/// - UserId НЯМА FK към AspNetUsers.Id — одитът трябва да оцелее при изтриване на
///   потребител (GDPR erasure);
/// - ClinicId НЯМА FK към Clinics.ClinicID — одитните редове изживяват изтрити клиники
///   (design doc §7: audit записът остава след ErasureService, Task 11).
/// </summary>
public class AuditLog
{
    public long AuditId { get; set; }

    /// <summary>null = операторско/без-контекст действие (няма tenant).</summary>
    public int? ClinicId { get; set; }

    /// <summary>ApplicationUser.Id (nvarchar(450)); null при анонимни действия (напр. login опити).</summary>
    public string? UserId { get; set; }

    /// <summary>Кратък код по списъка от Task 10 — напр. "login.failed", "patient.created".</summary>
    public string Action { get; set; } = null!;

    /// <summary>Засегнат тип — напр. "Patient", "Clinic", "Staff", "User".</summary>
    public string? Entity { get; set; }

    public int? EntityId { get; set; }

    public DateTime AtUtc { get; set; }
}
