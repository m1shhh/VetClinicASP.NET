using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VetClinic.Models.ViewModels;
using VetClinic.Services; 
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class PatientsController : Controller
    {
        private readonly IPatientService _patientService;

        // Конструктор: ASP.NET автоматично ще подаде услугата тук
        public PatientsController(IPatientService patientService)
        {
            _patientService = patientService;
        }

        // GET: Patients
        // Обновяваме метода Index
        public IActionResult Index(string searchString, string filterType, string sortOrder)
        {
            // Пази изборите във ViewBag, за да не се нулирът падащите менюта
            ViewBag.CurrentFilter = searchString;
            ViewBag.CurrentFilterType = string.IsNullOrEmpty(filterType) ? "name" : filterType;
            ViewBag.CurrentSort = string.IsNullOrEmpty(sortOrder) ? "asc" : sortOrder;

            // ВАЖНО Подават се на сървиса, за да си свърши работата!
            // Ако тук пише (null, null, null), търсачката няма да работи.
            var patients = _patientService.GetAllPatients(searchString, filterType, sortOrder);

            // Зареждаме статистиката за картите 
            ViewBag.TotalPatients = _patientService.GetAllPatients(null, null, null).Count();

            return View(patients);
        }
        // Формата за създаване на нов пациент
        [HttpGet]
        public IActionResult Create()
        {
            // Зареждане на собствениците за падащото меню
            var owners = _patientService.GetOwnersList();

            // Изпращат се към View-то чрез ViewBag
            ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");

            return View();
        }

        // POST: Приема данните и записва
        [HttpPost]
        public IActionResult Create(PatientInputModel model)
        {
            if (!ModelState.IsValid)
            {
                // Ако има грешка, връща се потребителя обратно във формата
                var owners = _patientService.GetOwnersList();
                ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");
                return View(model);
            }

            // Запис
            _patientService.AddPatient(model);

            // Връщане към списъка
            return RedirectToAction("Index");
        }
        public IActionResult Details(int id)
        {
            var patient = _patientService.GetPatientDetails(id);

            if (patient == null)
            {
                return NotFound();
            }

            return View(patient);
        }

        // GET: Отваря формата за редакция
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var model = _patientService.GetPatientForEdit(id);

            if (model == null)
            {
                return NotFound(); // Ако няма такъв пациент -> 404
            }

            // Зареждане на собствениците за падащото меню
            var owners = _patientService.GetOwnersList();
            ViewBag.Owners = new SelectList(owners, "Key", "Value", model.OwnerId);

            return View(model);
        }

        // POST: Записва промените
        [HttpPost]
        public IActionResult Edit(PatientInputModel model)
        {
            if (!ModelState.IsValid)
            {
                var owners = _patientService.GetOwnersList();
                ViewBag.Owners = new SelectList(owners, "Key", "Value", model.OwnerId);
                return View(model);
            }

            _patientService.UpdatePatient(model);

            return RedirectToAction("Details", new { id = model.Id });
        }
        // GET: Показва страница за потвърждение
        [HttpGet]
        public IActionResult Delete(int id)
        {

            var patient = _patientService.GetPatientDetails(id);

            if (patient == null)
            {
                return NotFound();
            }

            return View(patient);
        }

        // POST: Реалното изтриване
        [HttpPost, ActionName("Delete")]
        public IActionResult DeleteConfirmed(int id)
        {
            _patientService.DeletePatient(id);

            // Връщаме към началния списък (Home)
            return RedirectToAction("Index", "Home");
        }
    }
}