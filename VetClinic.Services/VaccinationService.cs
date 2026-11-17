using VetClinic.Models;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public class VaccinationService : IVaccinationService
    {
        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;

        public VaccinationService(VetClinicContext context, ICurrentClinic currentClinic)
        {
            _context = context;
            _currentClinic = currentClinic;
        }

        public void AddVaccination(VaccinationInputModel model)
        {
            var clinicId = _currentClinic.GetRequiredClinicId();

            // FK валидация през ФИЛТРИРАНИЯ контекст (деферълът от Task 5 ревю → Task 8):
            // пациентът трябва да е от ТЕКУЩАТА клиника — чужд/несъществуващ PatientId → throw.
            if (_context.Patients.Find(model.PatientId) == null)
            {
                throw new InvalidOperationException("Пациентът не е намерен.");
            }

            // VetId по подразбиране (Task 8): ветеринарен лекар → СОБСТВЕНИЯТ профил;
            // служебен персонал → NULL (формата за ваксинация няма избор на лекар, VetId е nullable).
            int? vetId = null;
            if (_currentClinic.VetProfileId.HasValue)
            {
                vetId = _currentClinic.VetProfileId.Value;
                // Евтина филтрирана проверка — остарял claim не може да линкне чужд/изтрит профил.
                if (_context.Veterinarians.Find(vetId.Value) == null)
                {
                    throw new InvalidOperationException("Ветеринарният лекар не е намерен.");
                }
            }

            var vaccine = new Vaccination
            {
                PatientId = model.PatientId,
                VaccineName = model.Name,

                VetId = vetId,

                // Конвертор от формата (DateTime) КЪМ базата (DateOnly)
                DateAdministered = DateOnly.FromDateTime(model.DateAdministered),

                NextDueDate = model.NextDueDate.HasValue
                    ? DateOnly.FromDateTime(model.NextDueDate.Value)
                    : null,

                ClinicId = clinicId
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