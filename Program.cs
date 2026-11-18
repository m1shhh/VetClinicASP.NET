using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VetClinic.Security;
using VetClinic.Services;
using VetClinicASP.NET.Infrastructure;
using VetClinicASP.NET.Models;
using VetClinic.Models;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// 1. ЗОНА ЗА РЕГИСТРАЦИЯ НА УСЛУГИ (SERVICES)
// Всички AddScoped и AddDbContext трябва да са ТУК (преди Build)
// =========================================================

// 1.1. Добавяне на MVC (Контролери и Views) + глобален ClinicActiveFilter (Task 9):
// деактивирана клиника → незабавен SignOut + redirect /Account/Locked при всяка заявка
// (skip: не-аутентикиран, Operator, /Account/*, без clinic_id claim).
builder.Services.AddControllersWithViews(o => o.Filters.Add<ClinicActiveFilter>());
builder.Services.AddScoped<IVeterinarianService, VeterinarianService>();
// ClinicActiveFilter кешира Clinic.IsActive за 60 s (IMemoryCache — singleton споделен между заявките).
builder.Services.AddMemoryCache();
// 1.2. Връзка с SQL Server База данни
builder.Services.AddDbContext<VetClinicContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 1.2.1. Криптиране при запис (Task 6): singleton, за да е ЕДИН ключ/инстанция за всички
// context-и — те го прихващат в ctor-а и строят конверторите в OnModelCreating
// (референтен модел: AzITi ApplicationDbContext). Ключът идва от
// Encryption:MasterKey (dev: user-secrets; prod: env var) — НИКОГА в кода/git.
builder.Services.AddSingleton<EncryptionService>(_ => new EncryptionService(builder.Configuration));

// 1.3. Тенант изолация: ICurrentClinic (claims → clinic_id) + DbContext с този контекст.
// Без тези регистрации AddDbContext не може да конструира context-а с tenant филтрите.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentClinic, CurrentClinic>();

// 1.3.1. Identity (Task 7): AddIdentityCore (без UI) + роли + EF stores върху VetClinicContext.
// Стойностите са точно по Global Constraints: пароли мин. 8 с главна/малка/цифра (без изискване
// за спец. символ); lockout 5 грешни опита → 15 мин; уникален имейл.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<VetClinicContext>()
.AddSignInManager()
.AddClaimsPrincipalFactory<ClinicUserClaimsPrincipalFactory>(); // "clinic_id" claim → ICurrentClinic

// 1.3.2. Authentication: бисквитката точно по Global Constraints (PetCare.Auth, HttpOnly,
// Secure=Always, SameSite=Lax, 8 ч sliding).
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, o =>
    {
        o.Cookie.Name = "PetCare.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

// 1.3. Регистриране на твоите Сървиси (Services)
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IVaccinationService, VaccinationService>(); // <-- Това добавихме последно
builder.Services.AddScoped<IOwnerService, OwnerService>();
// Персонал (Task 8): създаване/деактивиране на служители + линк към Veterinarian профил.
builder.Services.AddScoped<IStaffService, StaffService>();
// Операторски панел (Task 9): метаданни-само сервис — агрегати за всички клиники
// (IgnoreQueryFilters), деактивиране, provisioning. Никакъв API за tenant съдържание.
builder.Services.AddScoped<IOperatorMetadataService, OperatorMetadataService>();
// Одитен запис (Task 10): само кодове/Id-та — без PII и без IP. Scoped: пише през СЪЩИЯ
// scoped VetClinicContext, синхронно с бизнес действието.
builder.Services.AddScoped<IAuditService, AuditService>();
// GDPR erasure (Task 11): fan-out в ЕДНА транзакция + `clinic.deleted` вътре в нея
// (fail-closed за destructive операцията). Ползва същия scoped context.
builder.Services.AddScoped<IErasureService, ErasureService>();

// 1.4. Однократен поддръжков инструмент за криптиране на съществуващи данни (Task 6).
builder.Services.AddScoped<EncryptExistingDataService>();

// 1.5. Bootstrap seed (Task 9): роли (idempotent) + Operator акаунт САМО ако
// Operator:Email + Operator:InitialPassword са конфигурирани; иначе WARNING без crash.
// Регистриран е ПРЕДИ Build() и ПРЕДИ ранния return на --encrypt-existing, но hosted
// услуги стартират САМО при app.Run() — runner режимът (Build + return) НИКОГА не ги
// пуска, затова seed-ът не тича в maintenance прохода.
builder.Services.AddHostedService<OperatorSeedService>();

// =========================================================
// 2. ОДНОКРАТЕН РЕЖИМ: --encrypt-existing (Task 6)
// Консолен поддръжков проход: зарежда редовете на шестте tenant таблици
// (read passthrough на legacy plaintext) и ги криптира при запис. Излиза
// БЕЗ да стартира web сървъра. Idempotentен — второ пускане е no-op.
// =========================================================
if (args.Contains("--encrypt-existing"))
{
    var maintenanceApp = builder.Build();
    using (var scope = maintenanceApp.Services.CreateScope())
    {
        scope.ServiceProvider.GetRequiredService<EncryptExistingDataService>().Run();
    }

    return;
}

// =========================================================
// 3. СГЛОБЯВАНЕ НА ПРИЛОЖЕНИЕТО (BUILD)
// След този ред НЕ можеш да ползваш builder.Services!
// =========================================================
var app = builder.Build();

// =========================================================
// 4. КОНФИГУРАЦИЯ НА HTTP REQUEST PIPELINE
// =========================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Важно за зареждане на CSS, JS и картинки от wwwroot
app.UseStaticFiles();

app.UseRouting();

// Authentication ПРЕДИ Authorization (Task 7): без UseAuthentication HttpContext.User е анонимен
// дори с валидна бисквитка — и ICurrentClinic би останал без clinic_id claim.
app.UseAuthentication();

app.UseAuthorization();

// Ако ползваш .NET 9, тези редове са стандартни, но UseStaticFiles по-горе е по-важният
// за стандартните папки.
// app.MapStaticAssets(); 

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
// .WithStaticAssets(); // Можеш да го махнеш, ако дава грешка, но в .NET 9 е ОК.

app.Run();