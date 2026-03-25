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
        // 1. GET: Зарежда страницата за потвърждение (Delete.cshtml)
        [HttpGet]
        public IActionResult Delete(int id)
        {
            var owner = _ownerService.GetOwnerById(id);
            if (owner == null) return NotFound();

            return View(owner);
        }

        // 2. POST: Извършва реалното изтриване след натискане на червения бутон
        [HttpPost, ActionName("Delete")]
        public IActionResult DeleteConfirmed(int OwnerId)
        {
            _ownerService.DeleteOwner(OwnerId);
            return RedirectToAction("Index");
        }
        // ---------------------------------------------------------
        // НАЧАЛО НА EDIT МЕТОДИТЕ
        // ---------------------------------------------------------

        // 5. GET: Отваря формата за редакция с попълнени данни
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var owner = _ownerService.GetOwnerById(id);

            if (owner == null)
            {
                return NotFound();
            }

            return View(owner);
        }

        // 6. POST: Запазва промените в базата
        [HttpPost]
        public IActionResult Edit(Owner owner)
        {
            if (ModelState.IsValid)
            {
                _ownerService.UpdateOwner(owner);
                return RedirectToAction("Index");
            }

            // Ако има грешка във валидацията, връщаме потребителя във формата
            return View(owner);
        }
    }
}