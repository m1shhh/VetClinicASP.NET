using System.ComponentModel.DataAnnotations;

namespace VetClinic.Models.ViewModels
{
    public class AppointmentInputModel
    {
        public int PatientId { get; set; } // Скрито поле, за да знаем кой е пациентът

        [Required]
        [Display(Name = "Дата и час")]
        public DateTime DateTime { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "Моля, въведете причина или диагноза")]
        [Display(Name = "Причина / Диагноза")]
        public string Reason { get; set; } = null!;

        [Required(ErrorMessage = "Моля, изберете лекар")]
        [Display(Name = "Ветеринарен лекар")]
        public int VeterinarianId { get; set; }
        public int DurationMinutes { get; set; } = 30; // 30 е по подразбиране
        public int AppointmentId { get; set; }
    }
}