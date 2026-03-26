using Microsoft.EntityFrameworkCore;
using VetClinicASP.NET.Models;
using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public class AppointmentService : IAppointmentService
    {
        private readonly VetClinicContext _context;

        public AppointmentService(VetClinicContext context)
        {
            _context = context;
        }

        // ПОПРАВКА 1: Промени на IEnumerable<Appointment>
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

        // ПОПРАВКА 2: Промени на IEnumerable<KeyValuePair<int, string>>
        public IEnumerable<KeyValuePair<int, string>> GetDoctorsList()
        {
            return _context.Veterinarians
                .Select(v => new KeyValuePair<int, string>(v.VetId, v.FullName))
                .ToList();
        }

        // Останалите методи...
        public Appointment GetAppointmentById(int id)
        {
            return _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Vet)
                .FirstOrDefault(a => a.AppointmentId == id);
        }

        public void AddAppointment(AppointmentInputModel model)
        {
            var appointment = new Appointment
            {
                PatientId = model.PatientId,
                VetId = model.VeterinarianId,
                Reason = model.Reason,

                // Мапване на датата и часа
                AppointmentDate = DateOnly.FromDateTime(model.DateTime),
                AppointmentTime = TimeOnly.FromDateTime(model.DateTime),

                // НОВО: Вземаме маркираните минути от календара и ги пращаме в базата!
                DurationMinutes = model.DurationMinutes
            };

            _context.Appointments.Add(appointment);
            _context.SaveChanges();
        }
        public void DeleteAppointment(int id)
        {
            // 1. Търсим записа в базата данни по неговото ID
            var appointment = _context.Appointments.Find(id);

            // 2. Проверяваме дали записът съществува, преди да се опитаме да го изтрием
            if (appointment != null)
            {
                _context.Appointments.Remove(appointment);

                // 3. Запазваме промените в базата данни
                _context.SaveChanges();
            }
        }
        // 1. Взимаме записа и го превръщаме във ViewModel за формата
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

        // 2. Записваме промените върху съществуващия запис
        public void UpdateAppointment(AppointmentInputModel model)
        {
            var appointment = _context.Appointments.Find(model.AppointmentId);

            if (appointment != null)
            {
                appointment.PatientId = model.PatientId;
                appointment.VetId = model.VeterinarianId;
                appointment.Reason = model.Reason;
                appointment.DurationMinutes = model.DurationMinutes;
                appointment.AppointmentDate = DateOnly.FromDateTime(model.DateTime);
                appointment.AppointmentTime = TimeOnly.FromDateTime(model.DateTime);

                _context.SaveChanges();
            }
        }
    }
}