namespace VetClinic.Models.ViewModels
{
    public class PatientViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Species { get; set; } = null!; // Куче, Котка...
        public string Breed { get; set; } // Порода
        public int Age { get; set; }
        public string OwnerName { get; set; } = null!; // Тук ще вземем името от другата таблица
    }
}