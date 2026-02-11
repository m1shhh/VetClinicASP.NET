using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IAppointmentService
    {
        List<KeyValuePair<string, string>> GetDoctorsList();
        void AddAppointment(AppointmentInputModel model);

        List<VetClinicASP.NET.Models.Appointment> GetAllAppointments();
    }
}