using System.ComponentModel.DataAnnotations;

namespace VetClinic.Models.ViewModels
{
    public class PatientInputModel
    {
        [Required(ErrorMessage = "Името е задължително")]
        [Display(Name = "Име на пациента")]
        public string Name { get; set; } = null!;

        [Required]
        [Display(Name = "Вид (куче, котка...)")]
        public string Species { get; set; } = null!;

        [Display(Name = "Порода")]
        public string? Breed { get; set; }

        [Range(0, 30, ErrorMessage = "Възрастта трябва да е между 0 и 30")]
        [Display(Name = "Възраст")]
        public int Age { get; set; }

        [Required(ErrorMessage = "Моля, изберете собственик")]
        [Display(Name = "Собственик")]
        public int OwnerId { get; set; } // Ще пазим ID-то на избрания собственик
    }
}