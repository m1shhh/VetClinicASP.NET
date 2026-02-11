using Microsoft.EntityFrameworkCore;
using VetClinic.Models;
using VetClinic.Models.ViewModels;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public class PatientService : IPatientService
    {
        private readonly VetClinicContext _context;

        public PatientService(VetClinicContext context)
        {
            _context = context;
        }

        // 1. Списък с пациенти + Търсене
        public List<PatientViewModel> GetAllPatients(string? searchTerm = null)
        {
            var query = _context.Patients
                .Include(p => p.Owner)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(p =>
                    p.Name.Contains(searchTerm) ||
                    p.Owner.FullName.Contains(searchTerm)
                );
            }

            return query.Select(p => new PatientViewModel
            {
                Id = p.PatientId,
                Name = p.Name,
                Species = p.Species,
                Breed = p.Breed,
                Age = p.Age ?? 0,
                OwnerName = p.Owner != null ? p.Owner.FullName : "Няма собственик"
            }).ToList();
        }

        // 2. Добавяне
        public void AddPatient(PatientInputModel model)
        {
            var patient = new Patient
            {
                Name = model.Name,
                Species = model.Species,
                Breed = model.Breed,
                Age = model.Age,
                OwnerId = model.OwnerId
            };

            _context.Patients.Add(patient);
            _context.SaveChanges();
        }

        // 3. Списък собственици
        public List<KeyValuePair<string, string>> GetOwnersList()
        {
            return _context.Owners
                .Select(x => new KeyValuePair<string, string>(
                    x.OwnerId.ToString(),
                    x.FullName))
                .ToList();
        }

        // 4. Детайли (Досие)
        public PatientDetailsViewModel GetPatientDetails(int id)
        {
            var patient = _context.Patients
                .Include(p => p.Owner)
                .Include(p => p.Appointments).ThenInclude(a => a.Vet)
                .Include(p => p.Vaccinations)
                .Where(p => p.PatientId == id)
                .Select(p => new PatientDetailsViewModel
                {
                    Id = p.PatientId,
                    Name = p.Name,
                    Species = p.Species,
                    Breed = p.Breed,
                    Age = p.Age ?? 0,
                    OwnerName = p.Owner.FullName,
                    OwnerPhone = p.Owner.PhoneNumber,
                    OwnerAddress = p.Owner.Address,
                    Visits = p.Appointments.Select(a => new VisitViewModel
                    {
                        Date = a.AppointmentDate.ToDateTime(a.AppointmentTime),
                        Reason = a.Reason,
                        DoctorName = a.Vet != null ? a.Vet.FullName : "Неизвестен"
                    }).OrderByDescending(v => v.Date).ToList(),
                    Vaccinations = p.Vaccinations.Select(v => new VaccinationViewModel
                    {
                        VaccineName = v.VaccineName,
                        DateAdministered = v.DateAdministered.ToDateTime(TimeOnly.MinValue),
                        NextDueDate = v.NextDueDate.HasValue ? v.NextDueDate.Value.ToDateTime(TimeOnly.MinValue) : null
                    }).OrderByDescending(v => v.DateAdministered).ToList()
                })
                .FirstOrDefault();

            return patient;
        }

        // 5. Взимане за Редакция (GET)
        public PatientInputModel GetPatientForEdit(int id)
        {
            var patient = _context.Patients.Find(id);
            if (patient == null) return null;

            return new PatientInputModel
            {
                Id = patient.PatientId,
                Name = patient.Name,
                Species = patient.Species,
                Breed = patient.Breed,
                Age = patient.Age ?? 0,
                OwnerId = patient.OwnerId
            };
        }

        // 6. Запазване на Редакция (POST)
        public void UpdatePatient(PatientInputModel model)
        {
            var patient = _context.Patients.Find(model.Id);
            if (patient != null)
            {
                patient.Name = model.Name;
                patient.Species = model.Species;
                patient.Breed = model.Breed;
                patient.Age = model.Age;
                patient.OwnerId = model.OwnerId;
                _context.SaveChanges();
            }
        }
        // 7. Изтриване на пациент
        public void DeletePatient(int id)
        {
            var patient = _context.Patients.Find(id);

            if (patient != null)
            {
                // 1. Изтриваме Прегледите (Appointments)
                var appointments = _context.Appointments
                    .Where(a => a.PatientId == id)
                    .ToList();
                _context.Appointments.RemoveRange(appointments);

                // 2. Изтриваме Ваксините (Vaccinations)
                var vaccinations = _context.Vaccinations
                    .Where(v => v.PatientId == id)
                    .ToList();
                _context.Vaccinations.RemoveRange(vaccinations);

                // 3. НОВО: Изтриваме Медицинските записи (MedicalRecords)
                // Трябва да изчистим и тях, иначе базата няма да даде да изтрием пациента
                var records = _context.MedicalRecords
                    .Where(r => r.PatientId == id)
                    .ToList();
                _context.MedicalRecords.RemoveRange(records);

                // 4. Най-накрая трием самия пациент
                _context.Patients.Remove(patient);

                // 5. Запазваме всичко
                _context.SaveChanges();
            }
        }
    }
}