namespace VetClinic.Models.ViewModels
{
    public class PatientDetailsViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Species { get; set; } = null!;
        public string? Breed { get; set; }
        public int Age { get; set; }

        // Данни за собственика
        public string OwnerName { get; set; } = null!;
        public string? OwnerPhone { get; set; }
        public string? OwnerAddress { get; set; }

        // НОВО: Списък с история на прегледите
        public List<VisitViewModel> Visits { get; set; } = new List<VisitViewModel>();
    }

    // НОВО: Клас за един преглед
    public class VisitViewModel
    {
        public DateTime Date { get; set; }
        public string Reason { get; set; } // Причина / Диагноза
        public string DoctorName { get; set; } // Кой го е прегледал
    }
}