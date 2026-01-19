using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IPatientService
    {
        // Метод за взимане на всички пациенти за списъка
        List<PatientViewModel> GetAllPatients();

        // Тук по-късно ще добавим методи като:
        // void AddPatient(...);
        // PatientDetailsViewModel GetById(int id);
    }
}