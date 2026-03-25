using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IVaccinationService
    {
        // Съществуващият ти метод:
        void AddVaccination(VaccinationInputModel model);

        // НОВИТЕ МЕТОДИ:
        VaccinationInputModel GetVaccinationForEdit(int id);
        void UpdateVaccination(VaccinationInputModel model);
        void DeleteVaccination(int id);
        int GetPatientIdByVaccination(int id);
    }
}