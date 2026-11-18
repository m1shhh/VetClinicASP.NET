using Microsoft.EntityFrameworkCore;
using VetClinicASP.NET.Models;
using VetClinic.Models.ViewModels;
using VetClinic.Security;

namespace VetClinic.Services
{
    public class AppointmentService : IAppointmentService
    {
        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;

        public AppointmentService(VetClinicContext context, ICurrentClinic currentClinic)
        {
            _context = context;
            _currentClinic = currentClinic;
        }

        // взима всички срещи или часове
        public IEnumerable<Appointment> GetAllAppointments(int? vetId)
        {
            var query = _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Vet)
                .AsQueryable();

            if (vetId.HasValue)
            {
                query = query.Where(a => a.VetId == vetId.Value);
            }

            return query.OrderByDescending(a => a.AppointmentDate).ToList();
        }

        public IEnumerable<KeyValuePair<int, string>> GetDoctorsList()
        {
            return _context.Veterinarians
                .Select(v => new KeyValuePair<int, string>(v.VetId, v.FullName))
                .ToList();
        }

        public Appointment GetAppointmentById(int id)
        {
            return _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Vet)
                .FirstOrDefault(a => a.AppointmentId == id);
        }

        public void AddAppointment(AppointmentInputModel model)
        {
            var clinicId = _currentClinic.GetRequiredClinicId();

            // FK валидация през ФИЛТРИРАНИЯ контекст (деферълът от Task 5 ревю → Task 8):
            // пациентът трябва да е от ТЕКУЩАТА клиника — чужд/несъществуващ PatientId →
            // throw (същият стил като GetRequiredClinicId; контролерът го surface-ва като NotFound).
            if (_context.Patients.Find(model.PatientId) == null)
            {
                throw new InvalidOperationException("Пациентът не е намерен.");
            }

            int vetId;
            if (_currentClinic.VetProfileId.HasValue)
            {
                // Self-приписване (Task 8): ветеринарен лекар записва винаги на СОБСТВЕНИЯ си
                // профил — подаденият от формата VetId се ИГНОРИРА (не се валидира, не се приема).
                vetId = _currentClinic.VetProfileId.Value;
                // Евтина филтрирана проверка и за собствения профил: claim-ът идва от
                // аутентикацията, но проверката е евтина и хваща остарял/избит profile id.
                if (_context.Veterinarians.Find(vetId) == null)
                {
                    throw new InvalidOperationException("Ветеринарният лекар не е намерен.");
                }
            }
            else
            {
                // Receptionist/ClinicAdmin подава VetId изрично — валиден САМО сред клиниката
                // (Find върху филтрираното множество: чужд/несъществуващ лекар → throw).
                if (_context.Veterinarians.Find(model.VeterinarianId) == null)
                {
                    throw new InvalidOperationException("Ветеринарният лекар не е намерен.");
                }

                vetId = model.VeterinarianId;
            }

            var appointment = new Appointment
            {
                PatientId = model.PatientId,
                VetId = vetId,
                Reason = model.Reason,

                AppointmentDate = DateOnly.FromDateTime(model.DateTime),
                AppointmentTime = TimeOnly.FromDateTime(model.DateTime),

                DurationMinutes = model.DurationMinutes,
                ClinicId = clinicId
            };

            _context.Appointments.Add(appointment);
            _context.SaveChanges();
        }
        public void DeleteAppointment(int id)
        {
            // Търси се записа в базата данни по неговото ID
            var appointment = _context.Appointments.Find(id);

            // Проверява се дали записът съществува, преди да се опитаме да го изтрием
            if (appointment != null)
            {
                _context.Appointments.Remove(appointment);

                _context.SaveChanges();
            }
        }
        // Записват се данните за срещата, която ще се редактира, в AppointmentInputModel
        public AppointmentInputModel GetAppointmentForEdit(int id)
        {
            var appointment = _context.Appointments.Find(id);
            if (appointment == null) return null;

            return new AppointmentInputModel
            {
                AppointmentId = appointment.AppointmentId,
                PatientId = appointment.PatientId,
                VeterinarianId = appointment.VetId,
                Reason = appointment.Reason,
                DurationMinutes = appointment.DurationMinutes,
                // Сглобяваме DateOnly и TimeOnly обратно в пълен DateTime
                DateTime = appointment.AppointmentDate.ToDateTime(appointment.AppointmentTime)
            };
        }

        // Записване на промените върху съществуващия запис
        public void UpdateAppointment(AppointmentInputModel model)
        {
            var appointment = _context.Appointments.Find(model.AppointmentId);

            if (appointment != null)
            {
                // FK валидация през ФИЛТРИРАНИЯ контекст (Task 11 — деферъл от Task 8 ревю):
                // същият модел като AddAppointment. Проверките са ПРЕДИ присвояванията —
                // при отказ редът остава непроменен.
                if (_context.Patients.Find(model.PatientId) == null)
                {
                    throw new InvalidOperationException("Пациентът не е намерен.");
                }

                int vetId;
                if (_currentClinic.VetProfileId.HasValue)
                {
                    // Self-приписване и при редакция: ветеринарният лекар не може да
                    // прехвърли записа на друг профил — подаденият от формата VetId се
                    // ИГНОРИРА. Claim-ът е от аутентикацията; филтрираната проверка хваща
                    // остарял/избит профил.
                    vetId = _currentClinic.VetProfileId.Value;
                    if (_context.Veterinarians.Find(vetId) == null)
                    {
                        throw new InvalidOperationException("Ветеринарният лекар не е намерен.");
                    }
                }
                else
                {
                    // Receptionist/ClinicAdmin подава VetId изрично — валиден САМО сред
                    // клиниката (чужд/несъществуващ → throw).
                    if (_context.Veterinarians.Find(model.VeterinarianId) == null)
                    {
                        throw new InvalidOperationException("Ветеринарният лекар не е намерен.");
                    }

                    vetId = model.VeterinarianId;
                }

                appointment.PatientId = model.PatientId;
                appointment.VetId = vetId;
                appointment.Reason = model.Reason;
                appointment.DurationMinutes = model.DurationMinutes;
                appointment.AppointmentDate = DateOnly.FromDateTime(model.DateTime);
                appointment.AppointmentTime = TimeOnly.FromDateTime(model.DateTime);

                _context.SaveChanges();
            }
        }
    }
}