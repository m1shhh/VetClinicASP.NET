using System.ComponentModel.DataAnnotations;

namespace VetClinic.Models.ViewModels
{
    public class PatientInputModel
    {
        // НОВО: Това поле липсваше и затова дава грешката "does not contain definition for Id"
        public int Id { get; set; }

        [Required(ErrorMessage = "Името е задължително")]
        [Display(Name = "Име на пациента")]
        public string Name { get; set; } = null!;

        [Required]
        [Display(Name = "Вид животно")]
        public string Species { get; set; } = null!;

        [Display(Name = "Порода")]
        public string? Breed { get; set; }

        [Display(Name = "Възраст")]
        // Тук използваме int (цяло число). Ако в базата е null, ще го направим на 0.
        public int Age { get; set; }

        [Display(Name = "Собственик")]
        public int OwnerId { get; set; }
    }
}