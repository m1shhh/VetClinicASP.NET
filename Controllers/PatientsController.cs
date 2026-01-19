using Microsoft.AspNetCore.Mvc;
using VetClinic.Services; // За да виждаме интерфейса
using VetClinic.Models.ViewModels;
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
        public IActionResult Index()
        {
            // 1. Извикваме метода от услугата
            var allPatients = _patientService.GetAllPatients();

            // 2. Подаваме данните към View-то
            return View(allPatients);
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
    }
}