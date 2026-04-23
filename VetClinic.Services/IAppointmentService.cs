using System.Collections.Generic;
using VetClinicASP.NET.Models;
using VetClinic.Models.ViewModels;

namespace VetClinic.Services
{
    public interface IAppointmentService
    {
        IEnumerable<Appointment> GetAllAppointments(int? vetId);

        IEnumerable<KeyValuePair<int, string>> GetDoctorsList();

        void AddAppointment(AppointmentInputModel model);
        void DeleteAppointment(int id);
        Appointment GetAppointmentById(int id);
        AppointmentInputModel GetAppointmentForEdit(int id);
        void UpdateAppointment(AppointmentInputModel model);
    }
}