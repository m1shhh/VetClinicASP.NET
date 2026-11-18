using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using VetClinic.Models.ViewModels;
using VetClinic.Services;
using VetClinicASP.NET.Models; 

namespace VetClinic.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IPatientService _patientService;
        private readonly VetClinicContext _context; 
        private readonly IAuditService _auditService;

        // Обновявен конструктор , за да приема и Context
        public HomeController(
            ILogger<HomeController> logger,
            IPatientService patientService,
            VetClinicContext context,
            IAuditService auditService)
        {
            _logger = logger;
            _patientService = patientService;
            _context = context; // Запазваме го
            _auditService = auditService;
        }

        public IActionResult Index(string searchString, string filterType, string sortOrder)
        {
            // --- СТАТИСТИКА ЗА DASHBOARD (Винаги брои всички записи) ---
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

        // --- МЕТОДИ ЗА СЪЗДАВАНЕ НА ПАЦИЕНТ ---

        [HttpGet]
        public IActionResult Create()
        {
            var owners = _patientService.GetOwnersList();
            ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");

            // ПРОМЯНА: Указван пълния път до файла
            return View("~/Views/Patients/Create.cshtml");
        }

        [HttpPost]
        public IActionResult Create(PatientInputModel model)
        {
            if (!ModelState.IsValid)
            {
                var owners = _patientService.GetOwnersList();
                ViewBag.Owners = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(owners, "Key", "Value");

                // ПРОМЯНА: Връщам правилния файл + модела с грешките
                return View("~/Views/Patients/Create.cshtml", model);
            }

            // ТОВА е РЕАЛНИЯТ потребителски път за "Нов пациент" (бутонът във Views/Home/Index.cshtml
            // и формата в Views/Patients/Create.cshtml постват към /Home/Create).
            var patientId = _patientService.AddPatient(model);

            // Одит (Task 10): patient.created и тук — иначе реалният UI поток не оставя одитен
            // ред (PatientsController.Create е дублиращият път). Само код + Id, без PII.
            _auditService.Log("patient.created", "Patient", patientId);

            return RedirectToAction("Index");
        }
    }
}