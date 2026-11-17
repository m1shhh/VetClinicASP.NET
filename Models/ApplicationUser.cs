using Microsoft.AspNetCore.Identity;

namespace VetClinicASP.NET.Models;

/// <summary>
/// Identity потребител (Task 7). ClinicId е null САМО за роля Operator (design doc §3.3) —
/// deny-by-default query filter-ите (VetClinicContext.Tenancy.cs) не важат за Identity таблиците.
/// Не се обявява в отделен DbSet — IdentityDbContext<ApplicationUser> вече предоставя Users.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Клиниката на потребителя (null само за Operator).</summary>
    public int? ClinicId { get; set; }

    /// <summary>Показано име (български, задължително на ниво форма).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Деактивиран акаунт → login се отказва с обща грешка (без разкриване на причината).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>1:1 линк към Veterinarian профиля (за роля Vet — Task 8; Operator/ClinicAdmin/Receptionist = null).</summary>
    public int? VeterinarianId { get; set; }

    public virtual Clinic? Clinic { get; set; }
}
