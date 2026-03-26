using System.Collections.Generic;
using VetClinicASP.NET.Models;
using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IAppointmentService
    {
        // Увери се, че типът тук е точно IEnumerable<Appointment>
        IEnumerable<Appointment> GetAllAppointments(int? vetId);

        // Увери се, че типът тук е точно IEnumerable<KeyValuePair<int, string>>
        IEnumerable<KeyValuePair<int, string>> GetDoctorsList();

        void AddAppointment(AppointmentInputModel model);
        void DeleteAppointment(int id);
        Appointment GetAppointmentById(int id);
        AppointmentInputModel GetAppointmentForEdit(int id);
        void UpdateAppointment(AppointmentInputModel model);
    }
}