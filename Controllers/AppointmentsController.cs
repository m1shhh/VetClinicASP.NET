using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VetClinic.Services;
using VetClinic.Models.ViewModels;

namespace VetClinic.Controllers
{
    public class AppointmentsController : Controller
    {
        private readonly IAppointmentService _appointmentService;

        public AppointmentsController(IAppointmentService appointmentService)
        {
            _appointmentService = appointmentService;
        }

        // GET: Отваря формата. Получаваме patientId от бутона в досието
        [HttpGet]
        public IActionResult Create(int patientId)
        {
            var model = new AppointmentInputModel
            {
                PatientId = patientId,
                DateTime = DateTime.Now // Слагаме днешна дата по подразбиране
            };

            // Зареждаме лекарите
            var doctors = _appointmentService.GetDoctorsList();
            ViewBag.Doctors = new SelectList(doctors, "Key", "Value");

            return View(model);
        }

        // POST: Записва в базата
        [HttpPost]
        public IActionResult Create(AppointmentInputModel model)
        {
            if (!ModelState.IsValid)
            {
                var doctors = _appointmentService.GetDoctorsList();
                ViewBag.Doctors = new SelectList(doctors, "Key", "Value");
                return View(model);
            }

            _appointmentService.AddAppointment(model);

            // Връщаме се обратно в досието на конкретния пациент
            return RedirectToAction("Details", "Patients", new { id = model.PatientId });
        }
    }
}