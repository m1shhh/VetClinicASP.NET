using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VetClinic.Services;
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class VeterinariansController : Controller
    {
        private readonly IVeterinarianService _vetService;
        private readonly VetClinicContext _context;

        public VeterinariansController(IVeterinarianService vetService, VetClinicContext context)
        {
            _vetService = vetService;
            _context = context;
        }

        // Списък с лекари
        public IActionResult Index()
        {
            var vets = _vetService.GetAllVeterinarians();
            return View(vets);
        }

        //  Страница за добавяне 
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // 3. Запазване на новия лекар 
        [HttpPost]
        public IActionResult Create(Veterinarian vet)
        {
            if (ModelState.IsValid)
            {
                _vetService.AddVeterinarian(vet);
                return RedirectToAction("Index");
            }
            return View(vet);
        }

        // Зареждане на данните за редакция 
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var vet = _vetService.GetVeterinarianById(id);
            if (vet == null)
            {
                return NotFound();
            }
            return View(vet);
        }

        // Запазване на промените 
        [HttpPost]
        public IActionResult Edit(Veterinarian vet)
        {
            if (ModelState.IsValid)
            {
                _vetService.UpdateVeterinarian(vet);
                return RedirectToAction("Index");
            }
            return View(vet);
        }
        // Зареждане на страницата за потвърждение
        [HttpGet]
        public IActionResult Delete(int id)
        {
            var vet = _vetService.GetVeterinarianById(id);

            if (vet == null) return NotFound();

            return View(vet);
        }

        // POST: Извършва реалното изтриване
        [HttpPost, ActionName("Delete")]
        public IActionResult DeleteConfirmed(int VetId)
        {
            // Ownership през ФИЛТРИРАНИЯ контекст: чужд/несъществуващ VetId → no-op.
            var vet = _vetService.GetVeterinarianById(VetId);
            if (vet == null)
            {
                return RedirectToAction("Index");
            }

            // Guard (Task 11 — деферъл от Task 8 ревю): ApplicationUser.VeterinarianId няма
            // DB FK, т.е. изтриването на профила би оставило служебния акаунт със stale линк.
            // Затова профил, свързан с (какъвто и да е) акаунт, НЕ се трие — ясно съобщение.
            if (_context.Users.Any(u => u.VeterinarianId == VetId))
            {
                TempData["ErrorMessage"] =
                    "Ветеринарният профил е свързан със служебен акаунт и не може да бъде изтрит.";

                return RedirectToAction("Index");
            }

            _vetService.DeleteVeterinarian(VetId);
            return RedirectToAction("Index");
        }
    }
}