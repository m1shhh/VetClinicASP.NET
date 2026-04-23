using System.Collections.Generic;
using VetClinic.Models.ViewModels;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public interface IPatientService
    {
        IEnumerable<Patient> GetAllPatients(string searchString, string filterType, string sortOrder);
        void AddPatient(PatientInputModel model);
        List<KeyValuePair<string, string>> GetOwnersList();
        PatientDetailsViewModel GetPatientDetails(int id);

        // Методи за редакция
        PatientInputModel GetPatientForEdit(int id);
        void UpdatePatient(PatientInputModel model);

        //delete метода
        void DeletePatient(int id);
    }
}