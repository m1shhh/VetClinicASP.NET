using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IVaccinationService
    {
        void AddVaccination(VaccinationInputModel model);

        VaccinationInputModel GetVaccinationForEdit(int id);
        void UpdateVaccination(VaccinationInputModel model);
        void DeleteVaccination(int id);
        int GetPatientIdByVaccination(int id);
    }
}