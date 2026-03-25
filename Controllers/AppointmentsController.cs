using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VetClinic.Services;
using VetClinic.Models.ViewModels;

namespace VetClinic.Controllers
{
    public class AppointmentsController : Controller
    {
        private readonly IAppointmentService _appointmentService;
        private readonly IPatientService _patientService;

        // 1. НОВО: Добавяме поле за VetService
        private readonly IVeterinarianService _vetService;

        // 2. Обновяваме конструктора да приема и трите сервиза
        public AppointmentsController(
            IAppointmentService appointmentService,
            IPatientService patientService,
            IVeterinarianService vetService) // <--- Тук добавихме vetService
        {
            _appointmentService = appointmentService;
            _patientService = patientService;
            _vetService = vetService; // <--- Тук го присвояваме
        }

        // 3. INDEX: Списък с филтър
        public IActionResult Index(int? vetId)
        {
            // Тук вече _vetService съществува и няма да гърми!
            if (vetId.HasValue)
            {
                var vet = _vetService.GetVeterinarianById(vetId.Value);
                ViewBag.FilterName = vet != null ? $"на Д-р {vet.FullName}" : "";
                ViewBag.CurrentVetId = vetId;
            }
            else
            {
                ViewBag.FilterName = "";
                ViewBag.CurrentVetId = null;
            }

            var appointments = _appointmentService.GetAllAppointments(vetId);
            return View(appointments);
        }

        // 4. GET: Създаване
        [HttpGet]
        public IActionResult Create(int? patientId)
        {
            var model = new AppointmentInputModel
            {
                DateTime = DateTime.Now
            };

            if (patientId.HasValue)
            {
                model.PatientId = patientId.Value;
            }

            LoadDropdowns();
            return View(model);
        }

        // 5. POST: Запазване
        [HttpPost]
        public IActionResult Create(AppointmentInputModel model)
        {
            if (!ModelState.IsValid)
            {
                LoadDropdowns();
                return View(model);
            }

            _appointmentService.AddAppointment(model);
            return RedirectToAction("Index");
        }

        // 6. DELETE: Изтриване
        // 6. GET: Показва страницата за потвърждение за изтриване
        [HttpGet]
        public IActionResult Delete(int id)
        {
            // Намираме записа чрез сервиза, за да покажем детайлите на екрана
            // Увери се, че твоето View ползва модела, който връщаш тук
            var appointment = _appointmentService.GetAppointmentById(id);

            if (appointment == null)
            {
                return NotFound();
            }

            return View(appointment);
        }

        // 7. POST: Извършва реалното изтриване след натискане на червения бутон
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            _appointmentService.DeleteAppointment(id);
            return RedirectToAction("Index");
        }

        // Помощен метод
        private void LoadDropdowns()
        {
            var doctors = _appointmentService.GetDoctorsList();
            ViewBag.Doctors = new SelectList(doctors, "Key", "Value");

            var patients = _patientService.GetAllPatients(null);
            ViewBag.Patients = new SelectList(patients, "Id", "Name");
        }
    }
}