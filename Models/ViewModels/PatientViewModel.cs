namespace VetClinic.Models.ViewModels
{
    public class PatientViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Species { get; set; } = null!;
        public string Breed { get; set; }
        public int Age { get; set; }
        public string OwnerName { get; set; } = null!; // Използва се името от другата таблица
    }
}