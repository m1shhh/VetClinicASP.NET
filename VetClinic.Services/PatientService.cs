using Microsoft.EntityFrameworkCore; // Важно за .Include()
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

        // 1. Взимане на всички пациенти за таблицата
        public List<PatientViewModel> GetAllPatients()
        {
            return _context.Patients
                .Include(p => p.Owner)
                .Select(p => new PatientViewModel
                {
                    // ВНИМАНИЕ: Тук използваме PatientID, както се разбрахме
                    Id = p.PatientId,
                    Name = p.Name,
                    Species = p.Species,
                    Breed = p.Breed,
                    // Оправяме проблема с Nullable int (int?)
                    Age = p.Age ?? 0,
                    // Използваме FullName, както потвърди, че се казва колоната
                    OwnerName = p.Owner != null ? p.Owner.FullName : "Няма собственик"
                })
                .ToList();
        }

        // 2. Добавяне на нов пациент
        public void AddPatient(PatientInputModel model)
        {
            var patient = new Patient
            {
                Name = model.Name,
                Species = model.Species,
                Breed = model.Breed,
                Age = model.Age,
                // Тук свързваме външния ключ към собственика
                OwnerId = model.OwnerId
            };

            _context.Patients.Add(patient);
            _context.SaveChanges();
        }

        // 3. Взимане на списък със собственици за падащото меню
        public List<KeyValuePair<string, string>> GetOwnersList()
        {
            return _context.Owners
                .Select(x => new KeyValuePair<string, string>(
                    x.OwnerId.ToString(),
                    x.FullName))
                .ToList();
        }

        // 4. Взимане на детайли за "Досие"
        public PatientDetailsViewModel GetPatientDetails(int id)
        {
            var patient = _context.Patients
                .Include(p => p.Owner)
                .Include(p => p.Appointments) // 1. Взимаме прегледите
                    .ThenInclude(a => a.Vet) // 2. За всеки преглед взимаме лекаря
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

                    // 3. Пълним списъка с прегледи
                    // ... вътре в Select ...
                    Visits = p.Appointments.Select(a => new VisitViewModel
                    {
                        // 1. Събираме DateOnly и TimeOnly обратно в DateTime за изгледа
                        Date = a.AppointmentDate.ToDateTime(a.AppointmentTime),

                        Reason = a.Reason,

                        // 2. Връзката се казва "Vet", а не "Veterinarian"
                        DoctorName = a.Vet != null ? a.Vet.FullName : "Неизвестен"
                    })
.OrderByDescending(v => v.Date)
.ToList()
                    // ...
                })
                .FirstOrDefault();

            return patient;
        }
    }
}