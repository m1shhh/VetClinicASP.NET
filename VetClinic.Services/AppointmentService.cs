using VetClinic.Models;
using VetClinic.Models.ViewModels;
using VetClinicASP.NET.Models;


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
            // Взимаме лекарите за падащото меню
            // Провери дали ID-то е VeterinarianID или Id
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
                DateTime = model.DateTime,
                Reason = model.Reason
            };

            _context.Appointments.Add(appointment);
            _context.SaveChanges();
        }
    }
}