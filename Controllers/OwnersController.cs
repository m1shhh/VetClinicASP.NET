using Microsoft.AspNetCore.Mvc;
using VetClinic.Services;
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class OwnersController : Controller
    {
        private readonly IOwnerService _ownerService;

        public OwnersController(IOwnerService ownerService)
        {
            _ownerService = ownerService;
        }

        // 1. Списък със собственици
        public IActionResult Index()
        {
            var owners = _ownerService.GetAllOwners();
            return View(owners);
        }

        // 2. GET: Форма за нов собственик
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // 3. POST: Запазване на собственик
        [HttpPost]
        public IActionResult Create(Owner owner)
        {
            if (ModelState.IsValid)
            {
                _ownerService.AddOwner(owner);
                return RedirectToAction("Index");
            }
            return View(owner);
        }

        // 4. Изтриване
        public IActionResult Delete(int id)
        {
            _ownerService.DeleteOwner(id);
            return RedirectToAction("Index");
        }
    }
}