using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    /// </summary>
    [Authorize(Roles = AppRoles.ClinicAdmin)]
    public class ClinicAdminController : Controller
    {
        private readonly IStaffService _staffService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;

        public ClinicAdminController(
            IStaffService staffService,
            UserManager<ApplicationUser> userManager,
            IAuditService auditService)
        {
            _staffService = staffService;
            _userManager = userManager;
            _auditService = auditService;
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
