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
    }
}