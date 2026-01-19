using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IPatientService
    {
        // Метод за взимане на всички пациенти за списъка
        List<PatientViewModel> GetAllPatients();
        PatientDetailsViewModel GetPatientDetails(int id);
        // Добави тези два реда вътре в интерфейса
        void AddPatient(PatientInputModel model);
        List<KeyValuePair<string, string>> GetOwnersList(); // За падащото меню
        
    }
}