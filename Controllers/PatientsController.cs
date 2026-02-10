using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VetClinic.Models.ViewModels;
using VetClinic.Services; // За да виждаме интерфейса
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class PatientsController : Controller
    {
        // Тук декларираме, че имаме нужда от нашата услуга
        private readonly IPatientService _patientService;

        // Конструктор: ASP.NET автоматично ще подаде услугата тук
        public PatientsController(IPatientService patientService)
        {
            _patientService = patientService;
        }

        // GET: /Patients
        // GET: Patients
        public IActionResult Index(string searchString)
        {
            ViewBag.CurrentFilter = searchString;

            // ГРЕШКАТА ОБИКНОВЕНО Е ТУК:
            // Увери се, че в скобите подаваш (searchString)!
            // Ако пише само GetAllPatients(), то винаги търси "нищо" и връща всички записи.
            var patients = _patientService.GetAllPatients(searchString);

            return View(patients);
        }
        // 1. GET: Отваря празната форма
        [HttpGet]
        public IActionResult Create()
        {
            // Зареждаме собствениците за падащото меню
            var owners = _patientService.GetOwnersList();

            // Изпращаме ги към View-то чрез ViewBag
            ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");

            return View();
        }

        // 2. POST: Приема данните и записва
        [HttpPost]
        public IActionResult Create(PatientInputModel model)
        {
            if (!ModelState.IsValid)
            {
                // Ако има грешка, връщаме потребителя обратно във формата
                var owners = _patientService.GetOwnersList();
                ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");
                return View(model);
            }

            // Записваме
            _patientService.AddPatient(model);

            // Връщаме се към списъка
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
        // ---------------------------------------------------------
        // НАЧАЛО НА EDIT МЕТОДИТЕ (Сложи ги в PatientsController.cs)
        // ---------------------------------------------------------

        // GET: Отваря формата за редакция
        [HttpGet]
        public IActionResult Edit(int id)
        {
            // Използваме новия метод от сървиса
            var model = _patientService.GetPatientForEdit(id);

            if (model == null)
            {
                return NotFound(); // Ако няма такъв пациент -> 404
            }

            // Зареждаме собствениците за падащото меню
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

            // ВАРИАНТ 1: Връща те в Досието на животното (препоръчително след редакция)
            return RedirectToAction("Details", new { id = model.Id });
        }
        // ---------------------------------------------------------
        // КРАЙ НА EDIT МЕТОДИТЕ
        // ---------------------------------------------------------
    }
}