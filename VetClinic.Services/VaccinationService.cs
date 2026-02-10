using VetClinic.Models;
using VetClinic.Models.ViewModels;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public class VaccinationService : IVaccinationService
    {
        private readonly VetClinicContext _context;

        public VaccinationService(VetClinicContext context)
        {
            _context = context;
        }

        public void AddVaccination(VaccinationInputModel model)
        {
            var vaccine = new Vaccination
            {
                PatientId = model.PatientId,

                // ВАЖНО: Тук трябва да съвпада с името в PatientService (VaccineName)
                VaccineName = model.Name,

                // Конвертиране ОТ формата (DateTime) КЪМ базата (DateOnly)
                DateAdministered = DateOnly.FromDateTime(model.DateAdministered),

                NextDueDate = model.NextDueDate.HasValue
                    ? DateOnly.FromDateTime(model.NextDueDate.Value)
                    : null
            };

            _context.Vaccinations.Add(vaccine);
            _context.SaveChanges();
        }
    }
}