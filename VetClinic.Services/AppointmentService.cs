using System; // Важно за DateOnly и TimeOnly
using VetClinic.Models;
using VetClinic.Models.ViewModels;
using VetClinicASP.NET.Models;
using Microsoft.EntityFrameworkCore;

namespace VetClinic.Services
{
    public class AppointmentService : IAppointmentService
    {
        private readonly VetClinicContext _context;

        public AppointmentService(VetClinicContext context)
        {
            _context = context;
        }

        public List<KeyValuePair<string, string>> GetDoctorsList()
        {
            return _context.Veterinarians
                .Select(v => new KeyValuePair<string, string>(
                    v.VetId.ToString(),
                    v.FullName))
                .ToList();
        }

        public void AddAppointment(AppointmentInputModel model)
        {
            var appointment = new Appointment
            {
                PatientId = model.PatientId,
                VetId = model.VeterinarianId,

                // ПОПРАВКА: Разделяме DateTime на DateOnly и TimeOnly
                AppointmentDate = DateOnly.FromDateTime(model.DateTime),
                AppointmentTime = TimeOnly.FromDateTime(model.DateTime),

                Reason = model.Reason
            };

            _context.Appointments.Add(appointment);
            _context.SaveChanges();
        }

        // Най-горе добави: using Microsoft.EntityFrameworkCore;

        public List<VetClinicASP.NET.Models.Appointment> GetAllAppointments()
        {
            return _context.Appointments
                .Include(a => a.Patient) // Зареждаме данните за животното
                .Include(a => a.Vet)     // Зареждаме данните за лекаря
                .OrderByDescending(a => a.AppointmentDate) // Най-новите най-отгоре
                .ThenBy(a => a.AppointmentTime)
                .ToList();
        }
    }
}