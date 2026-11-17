using Microsoft.EntityFrameworkCore;
using VetClinic.Models;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public class PatientService : IPatientService
    {
        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;

        public PatientService(VetClinicContext context, ICurrentClinic currentClinic)
        {
            _context = context;
            _currentClinic = currentClinic;
        }

        // Списък с пациенти + Търсене
        public IEnumerable<Patient> GetAllPatients(string searchString, string filterType, string sortOrder)
        {
            bool isDesc = sortOrder == "desc";

            // Owner.FullName е КРИПТИРАНА колона (Task 6): EF би превел Contains/OrderBy
            // срещу ШИФРОТЕКСТА в SQL (тихо грешен резултат; при LIKE-превода EF дори
            // хвърля SqlException). Затова клонът "owner" материализира пациентите на
            // ТЕКУЩАТА клиника (global query filter-ът в VetClinicContext.Tenancy.cs вече е
            // ограничил набора — никога не излиза извън клиниката) и филтрира/сортира
            // В ПАМЕТА върху декриптираните стойности.
            // Компромис (design doc §3.2): пилотният мащаб е стотици до няколко хиляди
            // пациента на клиника → цената на in-memory филтъра е незабележима; печалбата —
            // коректно търсене/сортиране по криптирани имена. Не-криптираните колони
            // (Name/Breed/Age) остават на SQL ниво.
            if (filterType == "owner")
            {
                var ownersPatients = _context.Patients.Include(p => p.Owner).ToList();

                if (!string.IsNullOrEmpty(searchString))
                {
                    var needle = searchString.ToLower();
                    ownersPatients = ownersPatients
                        .Where(p => p.Owner != null
                                    && p.Owner.FullName != null
                                    && p.Owner.FullName.ToLower().Contains(needle))
                        .ToList();
                }

                return isDesc
                    ? ownersPatients.OrderByDescending(p => p.Owner?.FullName).ToList()
                    : ownersPatients.OrderBy(p => p.Owner?.FullName).ToList();
            }

            // Базова заявка с включен собственик
            var query = _context.Patients.Include(p => p.Owner).AsQueryable();

            // ФИЛТРИРАНЕ (Работи само ако има въведен текст)
            if (!string.IsNullOrEmpty(searchString))
            {
                searchString = searchString.ToLower();
                switch (filterType)
                {
                    case "breed":
                        query = query.Where(p => p.Breed != null && p.Breed.ToLower().Contains(searchString));
                        break;
                    case "age":
                        if (int.TryParse(searchString, out int age))
                            query = query.Where(p => p.Age == age);
                        break;
                    case "name":
                    default:
                        query = query.Where(p => p.Name.ToLower().Contains(searchString));
                        break;
                }
            }

            // СОРТИРАНЕ (Работи винаги, дори и без текст за търсене)
            switch (filterType)
            {
                case "breed":
                    query = isDesc ? query.OrderByDescending(p => p.Breed) : query.OrderBy(p => p.Breed);
                    break;
                case "age":
                    query = isDesc ? query.OrderByDescending(p => p.Age) : query.OrderBy(p => p.Age);
                    break;
                case "name":
                default:
                    query = isDesc ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name);
                    break;
            }

            return query.ToList();
        }
        public PatientDetailsViewModel GetPatientDetails(int id)
        {
            var patient = _context.Patients
                .Include(p => p.Owner)
                .Include(p => p.MedicalRecords).ThenInclude(mr => mr.Vet)
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

                    Visits = p.MedicalRecords.Select(mr => new VisitViewModel
                    {
                        RecordId = mr.RecordId,
                        Date = mr.RecordDate ?? DateTime.MinValue,
                        Reason = mr.Diagnosis,
                        Diagnosis = mr.Diagnosis,
                        Treatment = mr.Treatment,
                        Notes = mr.Notes,
                        DoctorName = mr.Vet != null ? mr.Vet.FullName : "Неизвестен"
                    }).OrderByDescending(v => v.Date).ToList(),

                    Vaccinations = p.Vaccinations.Select(v => new VaccinationViewModel
                    {
                        Id = v.VaccinationId,
                        VaccineName = v.VaccineName,
                        DateAdministered = v.DateAdministered.ToDateTime(TimeOnly.MinValue),
                        NextDueDate = v.NextDueDate.HasValue ? v.NextDueDate.Value.ToDateTime(TimeOnly.MinValue) : null
                    }).OrderByDescending(v => v.DateAdministered).ToList()
                })
                .FirstOrDefault();

            return patient;
        }
        // Добавяне
        public void AddPatient(PatientInputModel model)
        {
            var clinicId = _currentClinic.GetRequiredClinicId();

            // FK валидация през ФИЛТРИРАНИЯ контекст (деферълът от Task 5 ревю → Task 8):
            // стопанинът трябва да е от ТЕКУЩАТА клиника — чужд/несъществуващ OwnerId → throw
            // (Find върху филтрираното множество връща null и за двата случая).
            if (_context.Owners.Find(model.OwnerId) == null)
            {
                throw new InvalidOperationException("Стопанинът не е намерен.");
            }

            var patient = new Patient
            {
                Name = model.Name,
                Species = model.Species,
                Breed = model.Breed,
                Age = model.Age,
                OwnerId = model.OwnerId,
                ClinicId = clinicId
            };

            _context.Patients.Add(patient);
            _context.SaveChanges();
        }

        // Списък собственици
        public List<KeyValuePair<string, string>> GetOwnersList()
        {
            return _context.Owners
                .Select(x => new KeyValuePair<string, string>(
                    x.OwnerId.ToString(),
                    x.FullName))
                .ToList();
        }
        // Взимане за Редакция (GET)
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

        // Запазване на Редакция (POST)
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
        // Изтриване на пациент
        public void DeletePatient(int id)
        {
            var patient = _context.Patients.Find(id);

            if (patient != null)
            {
                // Изтриване на Прегледите (Appointments)
                var appointments = _context.Appointments
                    .Where(a => a.PatientId == id)
                    .ToList();
                _context.Appointments.RemoveRange(appointments);

                // Изтриване на ваксините (Vaccinations)
                var vaccinations = _context.Vaccinations
                    .Where(v => v.PatientId == id)
                    .ToList();
                _context.Vaccinations.RemoveRange(vaccinations);

                // Изтриване на Медицинските записи (MedicalRecords)
                var records = _context.MedicalRecords
                    .Where(r => r.PatientId == id)
                    .ToList();
                _context.MedicalRecords.RemoveRange(records);

                // изтриване на на пациента
                _context.Patients.Remove(patient);

                _context.SaveChanges();
            }
        }
    }
}