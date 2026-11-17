using System.ComponentModel.DataAnnotations;

namespace VetClinic.Models.ViewModels
{
    /// <summary>
    /// Форма за публична регистрация (Task 7): създава Clinic (Trial) + ClinicAdmin акаунт.
    /// ConsentAccepted НЕ е [Required] — съгласието се валидира СЪРВЪРНО (AccountController),
    /// урокът от privacy-audit-а на AzITi (design doc §7).
    /// </summary>
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Името на клиниката е задължително")]
        [StringLength(100)]
        [Display(Name = "Име на клиниката")]
        public string ClinicName { get; set; } = null!;

        [Required(ErrorMessage = "Вашето име е задължително")]
        [StringLength(100)]
        [Display(Name = "Вашето име")]
        public string DisplayName { get; set; } = null!;

        [Required(ErrorMessage = "Имейлът е задължителен")]
        [EmailAddress(ErrorMessage = "Невалиден имейл")]
        [Display(Name = "Имейл")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "Паролата е задължителна")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Паролата трябва да е поне 8 символа")]
        [DataType(DataType.Password)]
        [Display(Name = "Парола")]
        public string Password { get; set; } = null!;

        [Required(ErrorMessage = "Потвърдете паролата")]
        [Compare(nameof(Password), ErrorMessage = "Паролите не съвпадат")]
        [DataType(DataType.Password)]
        [Display(Name = "Потвърдете паролата")]
        public string ConfirmPassword { get; set; } = null!;

        [Display(Name = "Приемам условията за обработка на данни")]
        public bool ConsentAccepted { get; set; }
    }

    /// <summary>Форма за вход (Task 7). isPersistent е винаги false — 8 ч бисквитка.</summary>
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Имейлът е задължителен")]
        [EmailAddress(ErrorMessage = "Невалиден имейл")]
        [Display(Name = "Имейл")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "Паролата е задължителна")]
        [DataType(DataType.Password)]
        [Display(Name = "Парола")]
        public string Password { get; set; } = null!;
    }
}
