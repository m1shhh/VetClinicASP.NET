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
        private readonly IVeterinarianService _vetService;

        // Обновявен конструктора да приема и трите сервиза
        public AppointmentsController(
            IAppointmentService appointmentService,
            IPatientService patientService,
            IVeterinarianService vetService) 
        {
            _appointmentService = appointmentService;
            _patientService = patientService;
            _vetService = vetService; 
        }

        // INDEX: Списък с филтър
        public IActionResult Index(int? vetId)
        {
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

        // GET: Създаване и Отваря празната форма
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

            // ЗАДЪЛЖИТЕЛНО: Зареждаме падащите менюта, преди да покажем страницата. Работи не пипай
            LoadDropdowns();
            return View(model);
        }

        // POST: Приема данните и записва
        [HttpPost]
        public IActionResult Create(AppointmentInputModel model)
        {
            if (!ModelState.IsValid)
            {

                LoadDropdowns();

                return View(model);
            }

            try
            {
                _appointmentService.AddAppointment(model);
            }
            catch (InvalidOperationException ex)
            {
                // FK валидация в сервиса (Task 8): чужд/несъществуващ Patient/Vet → NotFound
                // (същият стил като MedicalRecordsController.Create от Task 5).
                return NotFound(ex.Message);
            }

            return RedirectToAction("Index");
        }

        // 6. DELETE: Изтриване и GET: Показва страницата за потвърждение за изтриване
        [HttpGet]
        public IActionResult Delete(int id)
        {
            // Намираме записа чрез сервиза, за да покажем детайлите на екрана
            var appointment = _appointmentService.GetAppointmentById(id);

            if (appointment == null)
            {
                return NotFound();
            }

            return View(appointment);
        }

        // POST: Извършва реалното изтриване след натискане на червения бутон
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

            var patients = _patientService.GetAllPatients(null, null, null);

            ViewBag.Patients = new SelectList(patients, "PatientId", "Name");
        }
        [HttpGet]
        public IActionResult GetBusyTimes(int vetId)
        {
            var appointments = _appointmentService.GetAllAppointments(vetId);

            var busySlots = appointments.Select(a => {
                // ВАЖНО: Създал съм истински DateTime обекти за да избегнем грешки с форматирането
                DateTime startDateTime = a.AppointmentDate.ToDateTime(a.AppointmentTime);

                DateTime endDateTime = startDateTime.AddMinutes(a.DurationMinutes);

                return new
                {
                    title = "Заето",
                    start = startDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    end = endDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    color = "#dc3545",
                    allDay = false
                };
            }).ToList();

            return Json(busySlots);
        }
        // GET: Отваря формата, попълнена със старите данни
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var model = _appointmentService.GetAppointmentForEdit(id);

            if (model == null)
            {
                return NotFound();
            }

            LoadDropdowns();
            return View(model);
        }

        // POST: Приема новите данни и презаписва
        [HttpPost]
        public IActionResult Edit(AppointmentInputModel model)
        {
            if (!ModelState.IsValid)
            {
                // Ако има грешка (напр. празно поле), зареждане на менютата пак
                LoadDropdowns();
                return View(model);
            }

            try
            {
                _appointmentService.UpdateAppointment(model);
            }
            catch (InvalidOperationException ex)
            {
                // FK валидация в сервиса (Task 11): чужд/несъществуващ Patient/Vet → NotFound
                // (същият стил като Create POST; БЕЗ 500).
                return NotFound(ex.Message);
            }

            return RedirectToAction("Index");
        }
    }
}