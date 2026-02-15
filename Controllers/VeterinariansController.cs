using Microsoft.AspNetCore.Mvc;
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

        // 1. Списък с лекари
        public IActionResult Index()
        {
            var vets = _vetService.GetAllVeterinarians();
            return View(vets);
        }

        // 2. Страница за добавяне (GET)
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // 3. Запазване на новия лекар (POST)
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

        // 1. Зареждане на данните за редакция (GET)
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

        // 2. Запазване на промените (POST)
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
        // Добави този метод в контролера:
        public IActionResult Delete(int id)
        {
            _vetService.DeleteVeterinarian(id);
            return RedirectToAction("Index");
        }
    }
}