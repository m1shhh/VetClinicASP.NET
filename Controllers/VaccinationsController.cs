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
        // ---------------------------------------------------------
        // НАЧАЛО НА EDIT МЕТОДИТЕ
        // ---------------------------------------------------------

        [HttpGet]
        public IActionResult Edit(int id)
        {
            var model = _vaccinationService.GetVaccinationForEdit(id);

            if (model == null)
            {
                return NotFound();
            }

            // This line tells MVC to look for Views/Vaccinations/Edit.cshtml
            return View(model);
        }

        [HttpPost]
        public IActionResult Edit(VaccinationInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Записваме промените чрез сървиса
            _vaccinationService.UpdateVaccination(model);

            // Връщаме се обратно в досието на пациента
            return RedirectToAction("Details", "Patients", new { id = model.PatientId });
        }

        // ---------------------------------------------------------
        // НАЧАЛО НА DELETE МЕТОДА
        // ---------------------------------------------------------

        // Използваме HttpGet, защото бутонът ни в HTML е <a> таг (линк)
        [HttpGet]
        public IActionResult Delete(int id)
        {
            // Трябва ни PatientId ПРЕДИ да изтрием ваксината, за да знаем къде да се върнем
            int patientId = _vaccinationService.GetPatientIdByVaccination(id);

            if (patientId == 0)
            {
                return NotFound();
            }

            // Трием ваксината
            _vaccinationService.DeleteVaccination(id);

            // Връщаме се в досието на животното
            return RedirectToAction("Details", "Patients", new { id = patientId });
        }
    }
}