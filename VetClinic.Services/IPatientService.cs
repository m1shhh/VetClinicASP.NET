using VetClinic.Models.ViewModels;
using System.Collections.Generic;

namespace VetClinic.Services
{
    public interface IPatientService
    {
        // Промени сигнатурата, за да приема filterType
        List<PatientViewModel> GetAllPatients(string searchString = null, string filterType = "name", string sortOrder = "asc");
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