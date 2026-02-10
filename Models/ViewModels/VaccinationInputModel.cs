using System.ComponentModel.DataAnnotations;

namespace VetClinic.Models.ViewModels
{
    public class VaccinationInputModel
    {
        public int PatientId { get; set; }

        [Required]
        [Display(Name = "Име на ваксината")]
        public string Name { get; set; } = null!;

        [Required]
        [Display(Name = "Дата на поставяне")]
        public DateTime DateAdministered { get; set; } = DateTime.Now;

        [Display(Name = "Дата за следваща доза")]
        public DateTime? NextDueDate { get; set; } // Може да е празно
    }
}