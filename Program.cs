using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
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

// 1.6. Login rate limit (Task 12, Global Constraints: 10/мин на IP → HTTP 429).
// Вградените middleware-и на .NET: fixed window, partition по клиентски IP.
// Политиката "login" се прилага САМО към POST Account/Login чрез [EnableRateLimiting].
// Ключът е "unknown" при липсващ RemoteIpAddress (напр. TestServer без forwarded headers) —
// всички такива заявки делят един bucket, което е безопасната посока.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// 1.7. ForwardedHeaders (Task 12, Global Constraints: САМО при ReverseProxy:Enabled=true).
// Зад Azure reverse proxy Connection.RemoteIpAddress е IP-то на proxy-то — без това
// всички клиенти падат в ЕДИН rate-limit bucket (Review Focus #5). KnownNetworks/
// KnownProxies се изчистват (proxy-то е доверено, мрежата на Azure не е фиксирана) —
// същият подход като AzITi. По подразбиране (dev) ключът липсва → middleware-ът НЕ се пуска.
var reverseProxyEnabled = string.Equals(
    builder.Configuration["ReverseProxy:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

if (reverseProxyEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// 1.8. Antiforgery X-Frame-Options: по подразбиране ASP.NET слага SAMEORIGIN при
// генериране на antiforgery token (form tag helper) — това би презаписало нашия
// X-Frame-Options: DENY в production. Заглавката е наша отговорност (Task 12):
// production → DENY; development → умишлено липсва (headers само в production).
builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);

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

// Точният CSP низ от плана (Global Constraints, ред 25) — ЕДИН ред, без преформатиране.
const string ContentSecurityPolicy =
    "default-src 'self'; img-src 'self' https:; style-src 'self' 'unsafe-inline'; " +
    "script-src 'self' 'unsafe-inline'; font-src 'self'; connect-src 'self'; " +
    "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

// 4.1. ForwardedHeaders ПЪРВИ (Task 12): поправя RemoteIpAddress/Scheme преди всичко,
// което чете request state (rate limiter-ът по-долу, HttpsRedirection, логирането).
if (reverseProxyEnabled)
{
    app.UseForwardedHeaders();

    // Production без изрично доверени proxy мрежи/IP-та → предупреждение (не crash):
    // изчистените KnownNetworks/KnownProxies означават „всяко proxy е доверено", което е
    // ОК само зад контролирания Azure front door.
    if (app.Environment.IsProduction()
        && !builder.Configuration.GetSection("ReverseProxy:KnownProxies").Exists()
        && !builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Exists())
    {
        app.Logger.LogWarning(
            "ForwardedHeaders са включени (ReverseProxy:Enabled=true) без изрични " +
            "ReverseProxy:KnownProxies/ReverseProxy:KnownNetworks — всички proxy-та са доверени.");
    }
}

// 4.2. Security headers САМО в production (като HSTS) — рано в pipeline-а, за да ги
// носят и error страниците, redirect-ите и статичните файлове. 'unsafe-inline' за
// style/script е документиран компромис (views-те имат inline блокове).
if (app.Environment.IsProduction())
{
    app.Use(async (context, next) =>
    {
        var response = context.Response;

        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;

        await next();
    });
}

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

// 4.3. Rate limiter (Task 12): СЛЕД ForwardedHeaders (виж 4.1) — partition-ът вижда
// реалния клиентски IP, не proxy-то. Политика "login" е само на POST Account/Login.
app.UseRateLimiter();

app.UseAuthorization();

// Ако ползваш .NET 9, тези редове са стандартни, но UseStaticFiles по-горе е по-важният
// за стандартните папки.
// app.MapStaticAssets(); 

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
// .WithStaticAssets(); // Можеш да го махнеш, ако дава грешка, но в .NET 9 е ОК.

app.Run();

// WebApplicationFactory<Program> (Task 12 тестове) изисква публичен тип за entry point-а
// на minimal hosting. Top-level statements генерират internal Program — това го прави видим.
public partial class Program { }