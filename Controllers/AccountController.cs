using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly VetClinicContext _context;
        private readonly IConfiguration _configuration;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            VetClinicContext context,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _configuration = configuration;
        }

        // =========================================================
        // REGISTER — публична регистрация зад toggle (Global Constraints:
        // Features:PublicRegistration = false по подразбиране). Създава
        // Clinic (Trial) + ClinicAdmin акаунт (design doc §3.3).
        // =========================================================

        [HttpGet]
        public IActionResult Register()
        {
            if (!IsPublicRegistrationEnabled())
            {
                return NotFound();
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!IsPublicRegistrationEnabled())
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // СЪРВЪРНА проверка на съгласието (НЕ [Required] атрибут — design doc §7):
            // отметката трябва да е проверена на сървъра, т.к. клиентската валидация се подмамя.
            if (!model.ConsentAccepted)
            {
                ModelState.AddModelError(string.Empty, "Трябва да приемете условията за обработка на данни.");
                return View(model);
            }

            // Атомарност (план, Task 7): Clinic + ApplicationUser + ролята се записват В ЕДНА
            // транзакция — при неуспешен CreateUser няма останал Clinic ред "сираче".
            ApplicationUser user;
            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                var clinic = new Clinic
                {
                    Name = model.ClinicName,
                    IsActive = true,
                    BillingStatus = ClinicBillingStatus.Trial,
                    CreatedAtUtc = DateTime.UtcNow,
                    ConsentGivenAtUtc = DateTime.UtcNow,
                    ConsentTextVersion = "v1"
                };
                _context.Clinics.Add(clinic);
                await _context.SaveChangesAsync();

                user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    DisplayName = model.DisplayName,
                    ClinicId = clinic.ClinicId,
                    IsActive = true
                };

                var result = await _userManager.CreateAsync(user, model.Password);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    return View(model);
                }

                // Резултатът от AddToRoleAsync НЕ се пренебрегва (Task 9, бележка (а) от
                // Task 7 ревю): провал → rollback + model error — няма акаунт без роля и
                // няма "сираче" Clinic ред след него.
                var roleResult = await _userManager.AddToRoleAsync(user, AppRoles.ClinicAdmin);
                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    foreach (var error in roleResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    return View(model);
                }

                await transaction.CommitAsync();
            }

            // Вход веднага след регистрацията (isPersistent: false — 8 ч бисквитка).
            await _signInManager.SignInAsync(user, isPersistent: false);

            return RedirectToAction("Index", "Patients");
        }

        // =========================================================
        // LOGIN — user enumeration защита: деактивиран/липсващ акаунт →
        // ЕДНАКЪВ общ меседж; lockout по Global Constraints (5 → 15 мин).
        // =========================================================

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !user.IsActive)
            {
                // НИКАКЪВ улчъшение кой от двата случая е — user enumeration защита.
                ModelState.AddModelError(string.Empty, "Невалидни данни за вход.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Patients");
            }

            if (result.IsLockedOut)
            {
                return RedirectToAction(nameof(Locked));
            }

            ModelState.AddModelError(string.Empty, "Невалидни данни за вход.");
            return View(model);
        }

        // =========================================================
        // LOCKED — статичен екран (lockout; Task 9 го преизползва и за
        // деактивирана клиника чрез ClinicActiveFilter).
        // =========================================================

        [HttpGet]
        public IActionResult Locked()
        {
            return View();
        }

        // =========================================================
        // LOGOUT — САМО POST с antiforgery (Task 9, бележка (б) от Task 7 ревю):
        // GET /Account/Logout повече НЕ съществува — линкът в _Layout е POST форма
        // с токен. Това е и правилният CSRF хигиенен образ на state-change endpoint.
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

            return RedirectToAction("Index", "Home");
        }

        private bool IsPublicRegistrationEnabled() =>
            string.Equals(_configuration["Features:PublicRegistration"], "true", StringComparison.OrdinalIgnoreCase);
    }
}
