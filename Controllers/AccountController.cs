using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinic.Services;
using VetClinicASP.NET.Models;

namespace VetClinic.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly VetClinicContext _context;
        private readonly IConfiguration _configuration;
        private readonly IAuditService _auditService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            VetClinicContext context,
            IConfiguration configuration,
            IAuditService auditService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _configuration = configuration;
            _auditService = auditService;
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

            // Guard (Task 12, разширение от Task 10 ревю): САМО анонимен може да регистрира.
            // Аутентикиран POST би наследил стария clinic_id claim → clinic.registered с
            // грешна ClinicId (и объркан tenant контекст). Избрано поведение: form + model
            // error (не redirect) — не изисква UrlHelper и остава разбираемо за потребителя.
            if (User?.Identity?.IsAuthenticated == true)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Вече сте влезли в профил — регистрацията е достъпна само за анонимни посетители.");
                return View(model);
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
            int registeredClinicId;
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
                registeredClinicId = clinic.ClinicId;

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

            // Одит (Task 10): clinic.registered — entityId е НОВИЯТ ClinicId. ClinicId полето
            // остава null (регистрантът е анонимен — още няма clinic_id claim). Без PII:
            // имейлът/името на админа НЕ се записват.
            _auditService.Log("clinic.registered", "Clinic", registeredClinicId);

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
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !user.IsActive)
            {
                // Одит (Task 10): провален опит — САМО код и Entity; имейлът НИКОГА не се
                // записва (entityId остава null — в него също не влиза имейл).
                _auditService.Log("login.failed", "User");

                // НИКАКЪВ улчъшение кой от двата случая е — user enumeration защита.
                ModelState.AddModelError(string.Empty, "Невалидни данни за вход.");
                return View(model);
            }

            // Преди опита: различаваме "5-ият грешен опит току-що заключи акаунта"
            // (login.failed + login.locked) от "опит върху вече заключен акаунт" (само
            // login.locked — паролата не е проверявана, затова няма фалшив login.failed).
            var wasLockedOut = await _userManager.IsLockedOutAsync(user);

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // UserId на реда остава null: одитът се пише ПРЕДИ SignInAsync, а
                // NameIdentifier claim-ът на заявката е още анонимен (без PII: имейл не се пише).
                _auditService.Log("login.success", "User");
                return RedirectToAction("Index", "Patients");
            }

            if (result.IsLockedOut)
            {
                if (!wasLockedOut)
                {
                    // 5-ият грешен опит: паролата се провали И акаунтът се заключи.
                    _auditService.Log("login.failed", "User");
                }

                _auditService.Log("login.locked", "User");
                return RedirectToAction(nameof(Locked));
            }

            // Грешна парола (акаунтът не е заключен): провал на идентификационните данни.
            _auditService.Log("login.failed", "User");
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
