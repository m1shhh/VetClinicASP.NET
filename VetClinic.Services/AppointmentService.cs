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
            // 1. Създаваме реалния обект за базата данни
            var appointment = new Appointment
            {
                PatientId = model.PatientId,
                VetId = model.VeterinarianId,
                Reason = model.Reason,

                // 2. РАЗДЕЛЯНЕ НА DATETIME: 
                // Ако твоята база ползва DateOnly и TimeOnly (net6.0+), правим това:
                AppointmentDate = DateOnly.FromDateTime(model.DateTime),
                AppointmentTime = TimeOnly.FromDateTime(model.DateTime)

                // ЗАБЕЛЕЖКА: Ако твоята база ползва стария DateTime формат за тези полета, 
                // просто използвай: AppointmentDate = model.DateTime;
            };

            // 3. Добавяне и запис
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

    }
}