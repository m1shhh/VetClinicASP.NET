using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinic.Services;

namespace VetClinic.Controllers
{
    /// <summary>
    /// Операторски панел (Task 9, design doc §3.3/§8): САМО за ролята Operator.
    /// Панелът работи ИЗЧЕРПИТЕЛНО с IOperatorMetadataService — метаданни и агрегати,
    /// НИКАКЪВ достъп до tenant съдържание (без имперсонация, без superadmin преглед).
    /// Деактивиране/активиране на клиника + създаване на пилотна клиника с първи
    /// ClinicAdmin акаунт. Route: конвенция като останалите контролери.
    /// </summary>
    [Authorize(Roles = AppRoles.Operator)]
    public class OperatorController : Controller
    {
        private readonly IOperatorMetadataService _metadataService;

        public OperatorController(IOperatorMetadataService metadataService)
        {
            _metadataService = metadataService;
        }

        // GET: Списък на всички клиники — метаданни + агрегатни брояци (без съдържание)
        [HttpGet]
        public IActionResult Index()
        {
            return View(_metadataService.GetClinics());
        }

        // POST: Активиране/деактивиране — посоката идва от формата (текущото състояние на реда)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetActive(int clinicId, bool active)
        {
            _metadataService.SetActive(clinicId, active);

            TempData["SuccessMessage"] = active
                ? "Клиниката беше активирана."
                : "Клиниката беше деактивирана — достъпът ѝ е отказан незабавно.";

            return RedirectToAction(nameof(Index));
        }

        // GET: Форма за нова пилотна клиника
        [HttpGet]
        public IActionResult NewClinic()
        {
            return View(new NewClinicViewModel());
        }

        // POST: Provisioning на Clinic (BillingStatus=Active) + ClinicAdmin — без преглед на данни
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult NewClinic(NewClinicViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var clinicId = _metadataService.CreatePilotClinic(
                    model.Name, model.AdminEmail, model.AdminDisplayName, model.AdminPassword);

                TempData["SuccessMessage"] =
                    $"Клиниката „{model.Name}“ беше създадена успешно с акаунт {model.AdminEmail}.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Identity провал (парола/дублиран имейл/роля) — транзакцията е rollback-ната,
                // без частично състояние; грешките се показват върху формата.
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }
    }
}
