using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VetClinic.Services;
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class VeterinariansController : Controller
    {
        private readonly IVeterinarianService _vetService;

        public VeterinariansController(IVeterinarianService vetService)
        {
            _vetService = vetService;
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
        [HttpGet]
        [HttpGet]
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
            _vetService.DeleteVeterinarian(VetId);
            return RedirectToAction("Index");
        }
    }
}