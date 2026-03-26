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
        // GET: Отваря празната форма
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

            // ЗАДЪЛЖИТЕЛНО: Зареждаме падащите менюта, преди да покажем страницата
            LoadDropdowns();
            return View(model);
        }

        // POST: Приема данните и записва
        [HttpPost]
        public IActionResult Create(AppointmentInputModel model)
        {
            if (!ModelState.IsValid)
            {
                // ПОПРАВКА: Ако има грешка, ТРЯБВА да заредим менютата отново,
                // иначе View-то ще гръмне с NullReferenceException!
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

            var patients = _patientService.GetAllPatients(null, null, null);

            // ПОПРАВКА: Сменихме "Id" на "PatientId", за да съвпада с базата данни!
            ViewBag.Patients = new SelectList(patients, "PatientId", "Name");
        }
        // Добави това в AppointmentsController.cs
        // Добави този метод в AppointmentsController.cs
        [HttpGet]
        public IActionResult GetBusyTimes(int vetId)
        {
            var appointments = _appointmentService.GetAllAppointments(vetId);

            var busySlots = appointments.Select(a => {
                // ВАЖНО: Създаваме истински DateTime обекти в C#, за да избегнем грешки с форматирането
                DateTime startDateTime = a.AppointmentDate.ToDateTime(a.AppointmentTime);

                // Добавяме минутите продължителност към началния час
                DateTime endDateTime = startDateTime.AddMinutes(a.DurationMinutes);

                return new
                {
                    title = "Заето",
                    // Форматираме ги гарантирано правилно (ISO 8601)
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
                // Ако има грешка (напр. празно поле), зареждаме менютата пак!
                LoadDropdowns();
                return View(model);
            }

            _appointmentService.UpdateAppointment(model);
            return RedirectToAction("Index");
        }
    }
}