using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using VetClinic.Models.ViewModels;
using VetClinic.Services;
using VetClinicASP.NET.Models; // За контекста и моделите

namespace VetClinic.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IPatientService _patientService;
        private readonly VetClinicContext _context; // 1. НОВО: Добавяме контекста за статистиката

        // 2. Обновяваме конструктора, за да приема и Context
        public HomeController(ILogger<HomeController> logger, IPatientService patientService, VetClinicContext context)
        {
            _logger = logger;
            _patientService = patientService;
            _context = context; // Запазваме го
        }

        public IActionResult Index(string searchString, string filterType, string sortOrder)
        {
            // --- 1. СТАТИСТИКА ЗА DASHBOARD (Винаги брои всички записи) ---
            ViewBag.TotalPatients = _context.Patients.Count();
            ViewBag.TotalDoctors = _context.Veterinarians.Count();

            var today = DateOnly.FromDateTime(DateTime.Now);
            ViewBag.AppointmentsToday = _context.Appointments
                .Where(a => a.AppointmentDate == today)
                .Count();
            // ---------------------------------------------------------------

            // --- 2. ЛОГИКА ЗА ТЪРСЕНЕ И ФИЛТРИРАНЕ НА ТАБЛИЦАТА ---

            // Пазим изборите, за да останат визуализирани в падащите менюта и полето за търсене
            ViewBag.CurrentFilter = searchString;
            ViewBag.CurrentFilterType = string.IsNullOrEmpty(filterType) ? "name" : filterType;
            ViewBag.CurrentSort = string.IsNullOrEmpty(sortOrder) ? "asc" : sortOrder;

            // Взимаме филтрирания списък (вече подаваме реалните променливи, а не null!)
            var patients = _patientService.GetAllPatients(searchString, filterType, sortOrder);
            // ---------------------------------------------------------------

            return View(patients);
        }
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // --- МЕТОДИ ЗА СЪЗДАВАНЕ НА ПАЦИЕНТ (Запазваме ги както са си) ---

        [HttpGet]
        public IActionResult Create()
        {
            var owners = _patientService.GetOwnersList();
            ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");

            // ПРОМЯНА: Указваме пълния път до файла
            return View("~/Views/Patients/Create.cshtml");
        }

        [HttpPost]
        public IActionResult Create(PatientInputModel model)
        {
            if (!ModelState.IsValid)
            {
                var owners = _patientService.GetOwnersList();
                ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");

                // ПРОМЯНА: Връщаме правилния файл + модела с грешките
                return View("~/Views/Patients/Create.cshtml", model);
            }

            _patientService.AddPatient(model);
            return RedirectToAction("Index");
        }
    }
}