using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IVaccinationService
    {
        void AddVaccination(VaccinationInputModel model);
    }
}