using System.ComponentModel.DataAnnotations;

namespace VetClinic.Models.ViewModels
{
    /// <summary>
    /// Форма "Нова пилотна клиника" (Task 9): Clinic (BillingStatus=Active) + ClinicAdmin
    /// акаунт. Операторът прави provisioning БЕЗ преглед на данни (design doc §3.3) —
    /// имейлът/паролата са за първия админ, никога не се логват от панела.
    /// </summary>
    public class NewClinicViewModel
    {
        [Required(ErrorMessage = "Името на клиниката е задължително")]
        [StringLength(100)]
        [Display(Name = "Име на клиниката")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Имейлът е задължителен")]
        [EmailAddress(ErrorMessage = "Невалиден имейл")]
        [Display(Name = "Имейл на администратора")]
        public string AdminEmail { get; set; } = null!;

        [Required(ErrorMessage = "Името на администратора е задължително")]
        [StringLength(100)]
        [Display(Name = "Име на администратора")]
        public string AdminDisplayName { get; set; } = null!;

        [Required(ErrorMessage = "Паролата е задължителна")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Паролата трябва да е поне 8 символа")]
        [DataType(DataType.Password)]
        [Display(Name = "Парола")]
        public string AdminPassword { get; set; } = null!;
    }
}
