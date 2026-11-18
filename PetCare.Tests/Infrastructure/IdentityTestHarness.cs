using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VetClinic.Security;
using VetClinic.Services;
using VetClinic.Controllers;
using VetClinicASP.NET.Models;
using Xunit;

namespace PetCare.Tests.Infrastructure
{
    /// <summary>
    /// Harness за AccountController (Task 7): ИСТИНСКИ UserManager/SignInManager над
    /// fixture-базиран VetClinicContext чрез ServiceCollection — AddIdentityCore със СЪЩИТЕ
    /// options като Program.cs (Global Constraints), EF stores, cookie auth и claims factory.
    /// SignInManager получава DefaultHttpContext с RequestServices = provider — SignInAsync
    /// работи in-process (Set-Cookie върху тестовия response), НУЛА stub-ове.
    ///
    /// Контекст-инстанцията е ЕДНА (scoped от root provider) за UserManager, UserStore и
    /// контролера — задължително, за да е атомарна транзакцията в Register POST.
    /// Ролите са системни справочници — ClinicAdmin се създава преди тестовете (Task 9
    /// ще ги seed-ва в production; тук е чрез RoleManager).
    /// </summary>
    internal static class IdentityTestHarness
    {
        public const string TestPassword = "Password1";

        /// <summary>
        /// ДЕТЕРМИНИСТИЧЕН IHttpContextAccessor: истинският HttpContextAccessor пази context
        /// в static AsyncLocal, чиято стойност не тече детерминирано от harness-setup-а
        /// през await-ите на тестовия метод към SignInManager (емпирично: null вътре в
        /// SignInAsync). Плоско инстанционна свойство — никакви ExecutionContext особености.
        /// </summary>
        private sealed class FixedHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext? HttpContext { get; set; }
        }

        public static async Task<Harness> BuildAsync(SqlTestDatabaseFixture fixture, bool publicRegistration)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Features:PublicRegistration"] = publicRegistration ? "true" : "false"
                })
                .Build();

            var httpContext = new DefaultHttpContext();
            var accessor = new FixedHttpContextAccessor { HttpContext = httpContext };

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHttpContextAccessor>(accessor);

            // Контекстът на теста е без клиничен контекст (анонимен посетител на Register/Login)
            // — Clinic/Identity таблиците са без филтър, затова създаването работи.
            services.AddSingleton<ICurrentClinic>(new TestClinicContext(null));
            services.AddSingleton(fixture.Encryption);
            services.AddSingleton(new DbContextOptionsBuilder<VetClinicContext>()
                .UseSqlServer(fixture.ConnectionString)
                .Options);
            services.AddScoped<VetClinicContext>(sp => new VetClinicContext(
                sp.GetRequiredService<DbContextOptions<VetClinicContext>>(),
                sp.GetRequiredService<ICurrentClinic>(),
                sp.GetRequiredService<EncryptionService>()));

            // Task 10: одитът пише през СЪЩИЯ scoped context (като в Program.cs) —
            // тестовете за login потока проверяват и одитните редове.
            services.AddScoped<IAuditService, AuditService>();

            // ТОЧНО същите options като Program.cs (Global Constraints) — тестовете доказват ТЯХ.
            services.AddIdentityCore<ApplicationUser>(options =>
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
            .AddClaimsPrincipalFactory<ClinicUserClaimsPrincipalFactory>();

            services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddCookie(IdentityConstants.ApplicationScheme, o =>
                {
                    o.Cookie.Name = "PetCare.Auth";
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    o.Cookie.SameSite = SameSiteMode.Lax;
                    o.SlidingExpiration = true;
                    o.ExpireTimeSpan = TimeSpan.FromHours(8);
                });

            var provider = services.BuildServiceProvider();
            httpContext.RequestServices = provider;

            // Системните роли (не tenant данни). Idempotentен guard на всяко изграждане.
            var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roleManager.RoleExistsAsync(AppRoles.ClinicAdmin))
            {
                await roleManager.CreateAsync(new IdentityRole(AppRoles.ClinicAdmin));
            }

            var controller = new AccountController(
                provider.GetRequiredService<UserManager<ApplicationUser>>(),
                provider.GetRequiredService<SignInManager<ApplicationUser>>(),
                provider.GetRequiredService<VetClinicContext>(),
                configuration,
                provider.GetRequiredService<IAuditService>());

            return new Harness(controller, provider, configuration);
        }

        public sealed record Harness(
            AccountController Controller,
            ServiceProvider Provider,
            IConfiguration Configuration) : IAsyncDisposable
        {
            /// <summary>Свеж контролер върху СЪЩИЯ provider (за многократни POST Login опити
            /// — ModelState-ът се трупа между извикванията на един контролер).</summary>
            public AccountController NewController() => new(
                Provider.GetRequiredService<UserManager<ApplicationUser>>(),
                Provider.GetRequiredService<SignInManager<ApplicationUser>>(),
                Provider.GetRequiredService<VetClinicContext>(),
                Configuration,
                Provider.GetRequiredService<IAuditService>());

            public ValueTask DisposeAsync() => Provider.DisposeAsync();
        }
    }
}
