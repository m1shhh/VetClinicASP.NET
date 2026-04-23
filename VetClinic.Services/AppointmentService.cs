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
            var appointment = new Appointment
            {
                PatientId = model.PatientId,
                VetId = model.VeterinarianId,
                Reason = model.Reason,

                AppointmentDate = DateOnly.FromDateTime(model.DateTime),
                AppointmentTime = TimeOnly.FromDateTime(model.DateTime),

                DurationMinutes = model.DurationMinutes
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