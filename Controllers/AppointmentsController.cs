using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VetClinic.Services;
using VetClinic.Models.ViewModels;

namespace VetClinic.Controllers
{
    public class AppointmentsController : Controller
    {
        private readonly IAppointmentService _appointmentService;
        private readonly IPatientService _patientService; // 1. НОВО: Трябва ни за списъка с пациенти

        // Инжектираме и двата сървиса
        public AppointmentsController(IAppointmentService appointmentService, IPatientService patientService)
        {
            _appointmentService = appointmentService;
            _patientService = patientService;
        }

        public IActionResult Index()
        {
            var appointments = _appointmentService.GetAllAppointments();
            return View(appointments);
        }

        // GET: Отваря формата
        // ПРОМЯНА: int? patientId (въпросителната значи, че може и да е null/празно)
        [HttpGet]
        public IActionResult Create(int? patientId)
        {
            var model = new AppointmentInputModel
            {
                DateTime = DateTime.Now // Слагаме днешна дата
            };

            // Ако имаме подадено ID (идваме от Досие), го попълваме предварително
            if (patientId.HasValue)
            {
                model.PatientId = patientId.Value;
            }

            // 2. ЗАРЕЖДАНЕ НА СПИСЪЦИТЕ (Dropdowns)
            LoadDropdowns();

            return View(model);
        }

        // POST: Записва в базата
        [HttpPost]
        public IActionResult Create(AppointmentInputModel model)
        {
            if (!ModelState.IsValid)
            {
                // Ако има грешка, презареждаме списъците, иначе ще гръмне View-то
                LoadDropdowns();
                return View(model);
            }

            _appointmentService.AddAppointment(model);

            // Връщаме се в Графика (защото е по-удобно да видиш новия час там)
            return RedirectToAction("Index");
        }

        // Помощен метод за зареждане на падащите менюта
        private void LoadDropdowns()
        {
            // Лекари
            var doctors = _appointmentService.GetDoctorsList();
            ViewBag.Doctors = new SelectList(doctors, "Key", "Value");

            // Пациенти (За да можем да избираме куче, ако не е избрано)
            // Тук ползваме GetAllPatients. Ако иска search term, подаваме null.
            var patients = _patientService.GetAllPatients(null);
            ViewBag.Patients = new SelectList(patients, "Id", "Name");
        }
    }
}