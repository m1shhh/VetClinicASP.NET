using VetClinic.Models.ViewModels;
using System.Collections.Generic;

namespace VetClinic.Services
{
    public interface IPatientService
    {
        List<PatientViewModel> GetAllPatients(string? searchTerm = null);
        void AddPatient(PatientInputModel model);
        List<KeyValuePair<string, string>> GetOwnersList();
        PatientDetailsViewModel GetPatientDetails(int id);

        // Методи за редакция
        PatientInputModel GetPatientForEdit(int id);
        void UpdatePatient(PatientInputModel model);

        //delete method
        void DeletePatient(int id);
    }
}