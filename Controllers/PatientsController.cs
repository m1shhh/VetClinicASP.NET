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
    }
}