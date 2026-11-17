using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VetClinic.Security;
using VetClinicASP.NET.Models;
using System;
using System.Linq;

namespace VetClinic.Controllers
{
    public class MedicalRecordsController : Controller
    {
        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;

        public MedicalRecordsController(VetClinicContext context, ICurrentClinic currentClinic)
        {
            _context = context;
            _currentClinic = currentClinic;
        }

        // GET: Зарежда формата за нов преглед
        [HttpGet]
        public IActionResult Create(int patientId)
        {
            // Проверка дали пациентът съществува
            var patient = _context.Patients.Find(patientId);
            if (patient == null)
            {
                return NotFound("Пациентът не е намерен.");
            }

            // Зареждане на списък с лекари за падащото меню
            ViewBag.Vets = new SelectList(_context.Veterinarians, "VetId", "FullName");

            // Записване на името на пациента, за да се покаже в заглавието на формата
            ViewBag.PatientName = patient.Name;

            // Подготвя се празен модел с вече попълнено ID на пациента и текуща дата
            var model = new MedicalRecord
            {
                PatientId = patientId,
                RecordDate = DateTime.Now
            };

            return View(model);
        }

        // POST: Запазва данните в базата
        [HttpPost]
        public IActionResult Create(MedicalRecord record)
        {
            // ИГНОРИРА НАВИГАЦИОННИТЕ СВОЙСТВА ЗА ДА МИНЕ ВАЛИДАЦИЯТА
            ModelState.Remove("Patient");
            ModelState.Remove("Vet");

            if (ModelState.IsValid)
            {
                // Тенант проверка: пациентът трябва да съществува И да е от текущата клиника.
                // Глобалният query filter скрива пациентите на другите клиники — Find върху
                // филтрираното множество връща null и за несъществуващ, и за чужд пациент.
                var patientInClinic = _context.Patients.Find(record.PatientId);
                if (patientInClinic == null)
                {
                    return NotFound("Пациентът не е намерен.");
                }

                // Self-приписване (Task 8): ветеринарен лекар записа винаги на СОБСТВЕНИЯ си
                // профил — формата не определя лектора. Служебен персонал подава VetId изрично —
                // валиден САМО сред клиниката (филтриран Find; липсващ → NotFound като горните).
                if (_currentClinic.VetProfileId.HasValue)
                {
                    var selfVet = _context.Veterinarians.Find(_currentClinic.VetProfileId.Value);
                    if (selfVet == null)
                    {
                        return NotFound("Ветеринарният лекар не е намерен.");
                    }

                    record.VetId = selfVet.VetId;
                }
                else if (record.VetId.HasValue && _context.Veterinarians.Find(record.VetId.Value) == null)
                {
                    return NotFound("Ветеринарният лекар не е намерен.");
                }

                record.ClinicId = _currentClinic.GetRequiredClinicId();
                _context.MedicalRecords.Add(record);
                _context.SaveChanges();

                // След успешен запис, връщаме лекаря обратно в досието на животното
                return RedirectToAction("Details", "Patients", new { id = record.PatientId });
            }

            // Ако има грешка, презареждаме лекарите и връщаме формата
            ViewBag.Vets = new SelectList(_context.Veterinarians, "VetId", "FullName", record.VetId);
            var patient = _context.Patients.Find(record.PatientId);
            ViewBag.PatientName = patient?.Name;

            return View(record);
        }

        // GET: Зарежда картона за редакция
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var record = _context.MedicalRecords.Find(id);
            if (record == null)
            {
                return NotFound("Картонът не е намерен.");
            }

            var patient = _context.Patients.Find(record.PatientId);
            ViewBag.PatientName = patient?.Name;
            ViewBag.Vets = new SelectList(_context.Veterinarians, "VetId", "FullName", record.VetId);

            return View(record);
        }

        // POST: Запазва промените
        [HttpPost]
        public IActionResult Edit(MedicalRecord record)
        {
            // ИГНОРИРАМЕ НАВИГАЦИОННИТЕ СВОЙСТВА ЗА ДА МИНЕ ВАЛИДАЦИЯТА (отново)
            ModelState.Remove("Patient");
            ModelState.Remove("Vet");

            if (ModelState.IsValid)
            {
                // Ownership през ФИЛТРИРАНИЯ контекст: чужд/несъществуващ RecordId → null → NotFound.
                var existing = _context.MedicalRecords.Find(record.RecordId);
                if (existing == null)
                {
                    return NotFound("Картонът не е намерен.");
                }

                existing.Diagnosis  = record.Diagnosis;
                existing.Treatment  = record.Treatment;
                existing.Notes      = record.Notes;
                existing.RecordDate = record.RecordDate;
                existing.VetId      = record.VetId;
                // ClinicId/PatientId остават непокътнати — query filter-ът пази само четения.
                _context.SaveChanges();

                // Връщаме се в досието на животното
                return RedirectToAction("Details", "Patients", new { id = existing.PatientId });
            }

            // Ако има грешка, презареждаме лекарите и формата
            ViewBag.Vets = new SelectList(_context.Veterinarians, "VetId", "FullName", record.VetId);
            return View(record);
        }

        // GET: Изтриване на картон
        public IActionResult Delete(int id)
        {
            var record = _context.MedicalRecords.Find(id);
            if (record != null)
            {
                int patientId = record.PatientId; // Запазва се ID-то на пациента, за да се върнем при него

                _context.MedicalRecords.Remove(record);
                _context.SaveChanges();

                // Връщаме се обратно в досието на животното
                return RedirectToAction("Details", "Patients", new { id = patientId });
            }

            return NotFound();
        }
    }
}