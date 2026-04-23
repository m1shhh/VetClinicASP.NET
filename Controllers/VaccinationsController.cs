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

            return RedirectToAction("Details", "Patients", new { id = model.PatientId });
        }
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var model = _vaccinationService.GetVaccinationForEdit(id);

            if (model == null)
            {
                return NotFound();
            }

            return View(model);
        }

        [HttpPost]
        public IActionResult Edit(VaccinationInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _vaccinationService.UpdateVaccination(model);

            return RedirectToAction("Details", "Patients", new { id = model.PatientId });
        }

        // Изтриване на ваксинации
        [HttpGet]
        public IActionResult Delete(int id)
        {
            // нужен е PatientId ПРЕДИ да се изтрие ваксината, за да знаем къде да се върнем
            int patientId = _vaccinationService.GetPatientIdByVaccination(id);

            if (patientId == 0)
            {
                return NotFound();
            }

            _vaccinationService.DeleteVaccination(id);

            // обратно в досието на животното
            return RedirectToAction("Details", "Patients", new { id = patientId });
        }
    }
}