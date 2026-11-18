using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinic.Services;
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    /// <summary>
    /// Персонал на клиниката (Task 8, design doc §5): само ClinicAdmin създава/деактивира
    /// служители (вкл. още ClinicAdmin-и — клиниката не бива да остава без админ).
    /// Изолацията е в IStaffService (изрични ClinicId филтри — Identity таблиците нямат
    /// query filter). Route: конвенция като останалите контролери ({controller}/{action}).
    ///
    /// Task 11: DeleteClinic (GDPR erasure) — двойно потвърждение с точно въведено име;
    /// clinicId идва САМО от ICurrentClinic, никога от формата.
    /// </summary>
    [Authorize(Roles = AppRoles.ClinicAdmin)]
    public class ClinicAdminController : Controller
    {
        private readonly IStaffService _staffService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;
        private readonly IErasureService _erasureService;
        private readonly ICurrentClinic _currentClinic;
        private readonly VetClinicContext _context;
        private readonly ILogger<ClinicAdminController> _logger;

        public ClinicAdminController(
            IStaffService staffService,
            UserManager<ApplicationUser> userManager,
            IAuditService auditService,
            IErasureService erasureService,
            ICurrentClinic currentClinic,
            VetClinicContext context,
            ILogger<ClinicAdminController> logger)
        {
            _staffService = staffService;
            _userManager = userManager;
            _auditService = auditService;
            _erasureService = erasureService;
            _currentClinic = currentClinic;
            _context = context;
            _logger = logger;
        }

        // GET: Служители — таблица + форма за създаване
        [HttpGet]
        public async Task<IActionResult> Users()
        {
            return View(await BuildUsersViewModel(new CreateStaffInputModel()));
        }

        // POST: Създаване на служител (паролната политика е на Identity — server-side)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStaff(CreateStaffInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Users", await BuildUsersViewModel(model));
            }

            var result = _staffService.CreateStaff(model.Email, model.DisplayName, model.Password, model.Role);
            if (!result.Succeeded)
            {
                // Identity грешки (парола, дублиран имейл, AddToRoleAsync провал...) → redisplay.
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View("Users", await BuildUsersViewModel(model));
            }

            // Одит (Task 10): staff.created. entityId е null ПО ЗАМИСЪЛ: UserId колоната носи
            // АКТОРА (клиничния админ), а самоличността на НОВИЯ служител (имейл/име — PII)
            // НИКОГА не влиза в одита. Entity "Staff" без id е достатъчно да се проследи
            // кой админ е създал персонал (същата конвенция като toggle кодовете).
            _auditService.Log("staff.created", "Staff");

            TempData["SuccessMessage"] = $"Служителят {model.Email} беше създаден успешно.";
            return RedirectToAction(nameof(Users));
        }

        // POST: Активиране/деактивиране — СРЕДНО на текущото състояние на акаунта
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(string userId)
        {
            // Текущото състояние определя посоката. userId от ЧУЖДА клиника се игнорира тихо
            // от SetStaffActive (изричният ClinicId филтър) — чужд акаунт не се докосва.
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                // Посоката се снема ПРЕДИ toggle-а: UserManager-ът и StaffService-ът делят
                // СЪЩИЯ scoped context, т.е. SetStaffActive мутира същия tracked instance —
                // след извикването user.IsActive вече е НОВОТО състояние.
                var wasActive = user.IsActive;
                var applied = _staffService.SetStaffActive(userId, !wasActive);

                // Одит САМО при реално записана промяна: FindByIdAsync връща потребители от
                // ВСЯКА клиника (Identity таблиците не са query-филтрирани), а SetStaffActive
                // тихо игнорира чужд/липсващ user или провален UpdateAsync (false) — без този
                // guard одитът би твърдял действие, което не се е случило.
                // entityId null: субектът (имейл/име) е PII — виж коментара в CreateStaff.
                if (applied)
                {
                    _auditService.Log(
                        wasActive ? "staff.deactivated" : "staff.activated",
                        "Staff");
                }
            }

            return RedirectToAction(nameof(Users));
        }

        // =========================================================
        // DELETE CLINIC — GDPR erasure (Task 11, design doc §7)
        // Двойно потвърждение: администраторът въвежда ТОЧНО името на клиниката.
        // ClinicId идва САМО от ICurrentClinic (claim) — никога от формата.
        // =========================================================

        [HttpGet]
        public IActionResult DeleteClinic()
        {
            var clinic = CurrentClinicOrNull();
            if (clinic == null)
            {
                return NotFound();
            }

            return View(new DeleteClinicViewModel { ClinicName = clinic.Name });
        }

        [HttpPost, ActionName("DeleteClinic")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteClinicConfirmed(string? confirmation)
        {
            // Ролята се проверява автоматично от [Authorize(Roles = ClinicAdmin)] на класа.
            // Клиниката се презарежда от DB и се сравнява с ТЕКУЩОТО ѝ име — подаденото от
            // формата име на клиника не се приема за истина.
            var clinic = CurrentClinicOrNull();
            if (clinic == null)
            {
                return NotFound();
            }

            var model = new DeleteClinicViewModel
            {
                ClinicName = clinic.Name,
                Confirmation = confirmation ?? string.Empty
            };

            if (!string.Equals(confirmation, clinic.Name, StringComparison.Ordinal))
            {
                ModelState.AddModelError(
                    nameof(DeleteClinicViewModel.Confirmation),
                    "Въведеното име не съвпада точно с името на клиниката. Изтриването не беше извършено.");

                return View("DeleteClinic", model);
            }

            try
            {
                // Fan-out + одит в ЕДНА транзакция (fail-closed) — вж. ErasureService.
                _erasureService.EraseClinic(clinic.ClinicId);
            }
            catch (Exception ex)
            {
                // Без PII в лога (само ClinicId). Не оставяме tracked Deleted Clinic ред в
                // контекста след rollback-а — следващ SaveChanges не бива да го изпълни пак.
                _logger.LogError(ex, "Изтриването на клиника {ClinicId} се провали.", clinic.ClinicId);
                _context.ChangeTracker.Clear();

                ModelState.AddModelError(
                    string.Empty,
                    "Изтриването не беше извършено поради техническа грешка. Опитайте отново или се свържете с поддръжката.");

                return View("DeleteClinic", model);
            }

            // Акаунтите на клиниката вече не съществуват — изчистваме бисквитката и
            // отвеждаме към заключения екран (същата UX като деактивирана клиника).
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

            return RedirectToAction("Locked", "Account");
        }

        /// <summary>Текущата клиника от claim-а (null при липсващ контекст/изтрит ред).</summary>
        private Clinic? CurrentClinicOrNull()
        {
            if (_currentClinic.ClinicId is not int clinicId)
            {
                return null;
            }

            return _context.Clinics.FirstOrDefault(c => c.ClinicId == clinicId);
        }

        // Помощен метод: таблица + ролеви dropdown + входната форма (със запазени стойности при грешка)
        private async Task<ClinicAdminUsersViewModel> BuildUsersViewModel(CreateStaffInputModel newStaff)
        {
            var rows = new List<StaffRowViewModel>();
            foreach (var user in _staffService.ListStaff())
            {
                var roles = await _userManager.GetRolesAsync(user);
                rows.Add(new StaffRowViewModel
                {
                    UserId = user.Id,
                    Email = user.Email ?? user.UserName ?? string.Empty,
                    DisplayName = user.DisplayName,
                    Role = roles.FirstOrDefault() ?? "—",
                    IsActive = user.IsActive
                });
            }

            return new ClinicAdminUsersViewModel
            {
                Staff = rows,
                NewStaff = newStaff,
                RoleOptions = new List<SelectListItem>
                {
                    new SelectListItem { Value = AppRoles.ClinicAdmin, Text = "Клиничен администратор" },
                    new SelectListItem { Value = AppRoles.Vet, Text = "Ветеринар" },
                    new SelectListItem { Value = AppRoles.Receptionist, Text = "Рецепционист(ка)" }
                }
            };
        }
    }
}
