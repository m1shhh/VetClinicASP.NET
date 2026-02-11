using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public interface IAppointmentService
    {
        List<KeyValuePair<string, string>> GetDoctorsList();

        // ПРОМЯНА: Правим параметъра vetId незадължителен (nullable)
        List<Appointment> GetAllAppointments(int? vetId = null);

        void AddAppointment(VetClinic.Models.ViewModels.AppointmentInputModel model);

        // НОВО: Метод за изтриване
        void DeleteAppointment(int id);
    }
}