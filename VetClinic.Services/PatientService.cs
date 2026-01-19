using Microsoft.EntityFrameworkCore; // За .Include()
using VetClinic.Models; // Тук са DB класовете
using VetClinic.Models.ViewModels;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public class PatientService : IPatientService
    {
        private readonly VetClinicContext _context;

        // Конструктор - тук "инжектираме" базата данни
        public PatientService(VetClinicContext context)
        {
            _context = context;
        }

        public List<PatientViewModel> GetAllPatients()
        {
            var patients = _context.Patients
                .Include(p => p.Owner)
                .Select(p => new PatientViewModel
                {
                    // ПОПРАВКА 1: Виж в Patient.cs как е ID-то.
                    // Ако е PatientId, напиши p.PatientId
                    Id = p.PatientId, // <-- Смени "PatientID" с точното име от файла!

                    // ПОПРАВКА 2: Име на животното
                    Name = p.Name,

                    // ПОПРАВКА 3: Оправяне на int? към int
                    // "?? 0" означава: ако в базата е NULL, използвай 0.
                    Age = p.Age ?? 0,

                    // ПОПРАВКА 4: Име на собственика
                    // Ако в Owner.cs нямаш "Name", а имаш FirstName и LastName, събери ги:
                    OwnerName = p.Owner != null
                        ? (p.Owner.FullName)
                        : "Няма собственик"
                })
                .ToList();

            return patients;
        }
    }
}