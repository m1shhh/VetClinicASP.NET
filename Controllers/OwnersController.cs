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

        // Списък със собственици
        public IActionResult Index()
        {
            var owners = _ownerService.GetAllOwners();
            return View(owners);
        }

        // GET: Форма за нов собственик
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Запазване на собственик
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

        // 4. Изтриване (Изглед за потвърждение)
        [HttpGet]
        public IActionResult Delete(int id)
        {
            var owner = _ownerService.GetOwnerById(id);
            if (owner == null) return NotFound();

            return View(owner);
        }

        // POST: Извършва реалното изтриване след натискане на червения бутон
        [HttpPost, ActionName("Delete")]
        public IActionResult DeleteConfirmed(int OwnerId)
        {
            _ownerService.DeleteOwner(OwnerId);
            return RedirectToAction("Index");
        }
        // GET: Отваря формата за редакция с попълнени данни
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

        // POST: Запазва промените в базата
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