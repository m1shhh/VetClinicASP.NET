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
                VaccineName = model.Name,

                // Конвертор от формата (DateTime) КЪМ базата (DateOnly)
                DateAdministered = DateOnly.FromDateTime(model.DateAdministered),

                NextDueDate = model.NextDueDate.HasValue
                    ? DateOnly.FromDateTime(model.NextDueDate.Value)
                    : null
            };

            _context.Vaccinations.Add(vaccine);
            _context.SaveChanges();
        }
        public VaccinationInputModel GetVaccinationForEdit(int id)
        {
            var vaccine = _context.Vaccinations.Find(id);
            if (vaccine == null) return null;

            return new VaccinationInputModel
            {
                VaccinationId = vaccine.VaccinationId, 
                PatientId = vaccine.PatientId,
                Name = vaccine.VaccineName,
                DateAdministered = vaccine.DateAdministered.ToDateTime(TimeOnly.MinValue),
                NextDueDate = vaccine.NextDueDate?.ToDateTime(TimeOnly.MinValue)
            };
        }

        public void UpdateVaccination(VaccinationInputModel model)
        {
            var vaccine = _context.Vaccinations.Find(model.VaccinationId);
            if (vaccine != null)
            {
                vaccine.VaccineName = model.Name;
                vaccine.DateAdministered = DateOnly.FromDateTime(model.DateAdministered);
                vaccine.NextDueDate = model.NextDueDate.HasValue
                    ? DateOnly.FromDateTime(model.NextDueDate.Value)
                    : null;

                _context.SaveChanges();
            }
        }

        public int GetPatientIdByVaccination(int id)
        {
            var vaccine = _context.Vaccinations.Find(id);
            return vaccine?.PatientId ?? 0;
        }

        public void DeleteVaccination(int id)
        {
            var vaccine = _context.Vaccinations.Find(id);
            if (vaccine != null)
            {
                _context.Vaccinations.Remove(vaccine);
                _context.SaveChanges();
            }
        }
    }
}