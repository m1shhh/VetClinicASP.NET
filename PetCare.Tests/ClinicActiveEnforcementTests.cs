using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using VetClinic.Security;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// ClinicActiveFilter (Task 9, design doc §8): деактивирана клиника = НЕЗАБАВЕН ефект —
    /// SignOut + redirect /Account/Locked при всяка заявка на клиничен потребител; статусът
    /// се чете от IMemoryCache с 60 s TTL (на miss → свеж scope → DB).
    /// Skip правила: не-аутентикиран, роля Operator, пътят /Account/*, без clinic_id claim
    /// (deny-by-default остава на query filter-ите).
    /// Филтърът се тества ДИРЕКТНО (AuthorizationFilterContext + DefaultHttpContext):
    /// RequestServices е ИСТИНСКИ provider (AddAuthentication+cookie + AddMemoryCache + fixture
    /// context) — задължително, защото SignOutAsync изисква регистрирани auth handler-и.
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class ClinicActiveEnforcementTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public ClinicActiveEnforcementTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeactivatedClinic_ClinicUser_RedirectsToLocked_AndCacheBlocksReactivationWithinWindow()
        {
            var clinicId = SeedClinic();
            await using var harness = await FilterHarness.BuildAsync(_fixture);
            SetClinicActiveInDb(clinicId, active: false);

            // 1-ва заявка: cache miss → DB → IsActive=false → SignOut + redirect /Account/Locked.
            var first = await harness.InvokeAsync(clinicId, "/Patients");
            AssertRedirectToLocked(first.Result);

            // Връщаме клиниката активна в БД — ВЪТРЕ в 60 s кеш прозореца.
            SetClinicActiveInDb(clinicId, active: true);

            // 2-ра заявка: кешът все още държи false → ПАК redirect — DB-staleness reversal
            // не променя изхода, докато кешът не изтече (защита срещу "мигащо" състояние).
            var second = await harness.InvokeAsync(clinicId, "/Patients");
            AssertRedirectToLocked(second.Result);
        }

        [Fact]
        public async Task ReactivatedClinic_FreshCacheWindow_RestoresAccess()
        {
            var clinicId = SeedClinic();
            SetClinicActiveInDb(clinicId, active: false);

            await using var blocked = await FilterHarness.BuildAsync(_fixture);
            AssertRedirectToLocked((await blocked.InvokeAsync(clinicId, "/Patients")).Result);

            SetClinicActiveInDb(clinicId, active: true);

            // Свеж кеш (еквивалент на ИЗТЕКЪЛ 60 s TTL — тестът не спи 60 s реално) →
            // DB read → IsActive=true → заявката преминава без redirect.
            await using var restored = await FilterHarness.BuildAsync(_fixture);
            Assert.Null((await restored.InvokeAsync(clinicId, "/Patients")).Result);
        }

        [Fact]
        public async Task ActiveClinic_ClinicUser_PassesThrough()
        {
            var clinicId = SeedClinic();
            await using var harness = await FilterHarness.BuildAsync(_fixture);

            var context = await harness.InvokeAsync(clinicId, "/Patients");

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task OperatorRole_DeactivatedClinic_PassesThrough()
        {
            var clinicId = SeedClinic();
            SetClinicActiveInDb(clinicId, active: false);
            await using var harness = await FilterHarness.BuildAsync(_fixture);

            var context = await harness.InvokeAsync(clinicId, "/Operator", role: AppRoles.Operator);

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task Unauthenticated_Request_PassesThrough()
        {
            // Skip правило: не-аутентикиран → филтърът не барикира (login страницата трябва да рендери).
            await using var harness = await FilterHarness.BuildAsync(_fixture);

            var context = await harness.InvokeAsync(clinicId: null, "/Patients", role: null);

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task AccountPath_DeactivatedClinicUser_PassesThrough()
        {
            // Skip правило: /Account/* (login/register/locked) работи ВИНАГИ — иначе блокираният
            // потребител не би могъл да стигне до екрана "клиниката е деактивирана".
            var clinicId = SeedClinic();
            SetClinicActiveInDb(clinicId, active: false);
            await using var harness = await FilterHarness.BuildAsync(_fixture);

            var context = await harness.InvokeAsync(clinicId, "/Account/Login");

            Assert.Null(context.Result);
        }

        // =========================================================
        // Помощни методи
        // =========================================================

        private static void AssertRedirectToLocked(IActionResult? result)
        {
            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/Account/Locked", redirect.Url);
        }

        private int SeedClinic()
        {
            using var raw = CreateRawContext();
            var clinic = new Clinic
            {
                Name = $"Клиника {UniqueSuffix()}",
                CreatedAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            raw.Clinics.Add(clinic);
            raw.SaveChanges();
            return clinic.ClinicId;
        }

        private void SetClinicActiveInDb(int clinicId, bool active)
        {
            using var raw = CreateRawContext();
            var clinic = raw.Clinics.Single(c => c.ClinicId == clinicId);
            clinic.IsActive = active;
            raw.SaveChanges();
        }

        private VetClinicContext CreateRawContext() =>
            _fixture.CreateContext(new TestClinicContext(null));

        private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];

        /// <summary>
        /// Harness за ClinicActiveFilter: ИСТИНСКИ provider (memory cache + auth cookie +
        /// VetClinicContext за свежите scope-четения на Clinic.IsActive). Филтърът се
        /// конструира с singleton-безопасни зависимости (IMemoryCache + IServiceScopeFactory) —
        /// НЕ държи scoped DbContext.
        /// </summary>
        private static class FilterHarness
        {
            public static async Task<Harness> BuildAsync(SqlTestDatabaseFixture fixture)
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<ICurrentClinic>(new TestClinicContext(null));
                services.AddSingleton(fixture.Encryption);
                services.AddSingleton(new DbContextOptionsBuilder<VetClinicContext>()
                    .UseSqlServer(fixture.ConnectionString)
                    .Options);
                services.AddScoped<VetClinicContext>(sp => new VetClinicContext(
                    sp.GetRequiredService<DbContextOptions<VetClinicContext>>(),
                    sp.GetRequiredService<ICurrentClinic>(),
                    sp.GetRequiredService<EncryptionService>()));

                // SignOutAsync изисква регистрирани auth handler-и — същият cookie като Program.cs.
                services.AddAuthentication(IdentityConstants.ApplicationScheme)
                    .AddCookie(IdentityConstants.ApplicationScheme, o =>
                    {
                        o.Cookie.Name = "PetCare.Auth";
                    });

                var provider = services.BuildServiceProvider();
                var filter = new ClinicActiveFilter(
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IServiceScopeFactory>());

                return new Harness(provider, filter);
            }

            public sealed record Harness(ServiceProvider Provider, ClinicActiveFilter Filter) : IAsyncDisposable
            {
                public async Task<AuthorizationFilterContext> InvokeAsync(
                    int? clinicId, string path, string? role = AppRoles.ClinicAdmin)
                {
                    var claims = new List<Claim>();
                    if (clinicId is int id)
                    {
                        claims.Add(new Claim("clinic_id", id.ToString()));
                    }
                    if (role is string roleName)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, roleName));
                    }

                    var httpContext = new DefaultHttpContext { RequestServices = Provider };
                    httpContext.Request.Path = path;
                    httpContext.User = claims.Count > 0
                        ? new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                        : new ClaimsPrincipal(new ClaimsIdentity()); // анонимен

                    var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
                    var filterContext = new AuthorizationFilterContext(actionContext, Array.Empty<IFilterMetadata>());
                    await Filter.OnAuthorizationAsync(filterContext);
                    return filterContext;
                }

                public ValueTask DisposeAsync() => Provider.DisposeAsync();
            }
        }
    }
}
