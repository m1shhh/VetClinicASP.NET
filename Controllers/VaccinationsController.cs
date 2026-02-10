using Microsoft.AspNetCore.Mvc;
using VetClinic.Services;
using VetClinic.Models.ViewModels;

namespace VetClinic.Controllers
{
    public class VaccinationsController : Controller
    {
        private readonly IVaccinationService _vaccinationService;

        public VaccinationsController(IVaccinationService vaccinationService)
        {
            _vaccinationService = vaccinationService;
        }

        [HttpGet]
        public IActionResult Create(int patientId)
        {
            return View(new VaccinationInputModel
            {
                PatientId = patientId,
                DateAdministered = DateTime.Now,
                NextDueDate = DateTime.Now.AddYears(1)
            });
        }

        [HttpPost]
        public IActionResult Create(VaccinationInputModel model)
        {
            if (!ModelState.IsValid) return View(model);

            _vaccinationService.AddVaccination(model);

            // Връща ни обратно в досието на пациента
            return RedirectToAction("Details", "Patients", new { id = model.PatientId });
        }
    }
}