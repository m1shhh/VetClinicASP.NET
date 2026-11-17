using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace VetClinic.Models.ViewModels
{
    /// <summary>
    /// ViewModels за ClinicAdminController/Users (Task 8). Ролевият dropdown е СТРОГО
    /// трите клинични роли — Operator не се създава от клиниките (Task 9 bootstrap seed).
    /// </summary>
    public class ClinicAdminUsersViewModel
    {
        public List<StaffRowViewModel> Staff { get; set; } = new();

        /// <summary>Формата "Добави служител" (пази стойностите при redisplay след грешка).</summary>
        public CreateStaffInputModel NewStaff { get; set; } = new();

        public List<SelectListItem> RoleOptions { get; set; } = new();
    }

    /// <summary>Един ред от таблицата със служители (ролята е resolved през UserManager).</summary>
    public class StaffRowViewModel
    {
        public string UserId { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    /// <summary>Вход за CreateStaff. Паролата се валидира от Identity политиката на сървъра.</summary>
    public class CreateStaffInputModel
    {
        [Required(ErrorMessage = "Моля, въведете имейл.")]
        [EmailAddress(ErrorMessage = "Невалиден имейл адрес.")]
        [Display(Name = "Имейл")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Моля, въведете име.")]
        [Display(Name = "Име")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Моля, въведете парола.")]
        [DataType(DataType.Password)]
        [Display(Name = "Парола")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Моля, изберете роля.")]
        [Display(Name = "Роля")]
        public string Role { get; set; } = string.Empty;
    }
}
