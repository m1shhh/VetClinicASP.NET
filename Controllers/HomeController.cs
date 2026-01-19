using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using VetClinic.Models.ViewModels;
using VetClinic.Services; // Важно: за да ползваме услугата
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IPatientService _patientService; // 1. Дефинираме услугата

        // 2. Инжектираме услугата през конструктора
        public HomeController(ILogger<HomeController> logger, IPatientService patientService)
        {
            _logger = logger;
            _patientService = patientService;
        }

        public IActionResult Index()
        {
            // 3. Взимаме пациентите от базата
            var patients = _patientService.GetAllPatients();

            // 4. ПОДАВАМЕ ги на View-то. Това оправя грешката "Model is null"
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
    }
}