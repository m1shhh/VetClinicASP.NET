using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VetClinic.Controllers;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinic.Services;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Одитен запис (Task 10, design doc §4/§7): редовете съдържат САМО кодове и Id-та —
    /// никакво PII (имена/имейли/пароли) и никакви IP адреси. Проверяват се:
    /// - ClinicId/UserId/Action/Entity/EntityId/AtUtc се записват коректно (от
    ///   ICurrentClinic + NameIdentifier claim наHttpContext.User);
    /// - без клиничен контекст → ClinicId NULL (операторски действия);
    /// - login потокът: 5 грешни опита → 5 реда login.failed + 1 login.locked
    ///   (5-тият опит и проваля паролата, и заключва акаунта); опит върху вече
    ///   заключен акаунт → САМО login.locked; успешен вход → login.success;
    /// - пациентски контролер: patient.created / patient.deleted с entityId;
    /// - action > 50 знака → ТРУНКВАНЕ до 50 (одитът не смутява бизнес операцията);
    /// - AuditLog НЕ е tenant entity: няма query filter — и raw (null), и tenant
    ///   контекст четат ВСИЧКИ редове (zero-row denial не се отнася за одита).
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class AuditServiceTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public AuditServiceTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        // =========================================================
        // Test 1: правилни ClinicId/UserId/Action/AtUtc
        // =========================================================

        [Fact]
        public async Task Log_WritesRowWithClinicContextAndPrincipalUserId()
        {
            var sinceId = await MaxAuditIdAsync();
            var currentClinic = new TestClinicContext(10);

            await using var context = _fixture.CreateContext(currentClinic);
            var before = DateTime.UtcNow;

            new AuditService(context, currentClinic, AccessorWithUser("user-abc-123"))
                .Log("patient.created", "Patient", 42);

            var row = Assert.Single(await NewRowsAsync(sinceId));

            Assert.Equal(10, row.ClinicId);
            Assert.Equal("user-abc-123", row.UserId);
            Assert.Equal("patient.created", row.Action);
            Assert.Equal("Patient", row.Entity);
            Assert.Equal(42, row.EntityId);
            Assert.InRange(row.AtUtc, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(2));
        }

        // =========================================================
        // Test 2: без клиничен контекст → ClinicId NULL (операторска симулация)
        // =========================================================

        [Fact]
        public async Task Log_WithoutClinicContext_WritesNullClinicIdRow()
        {
            var sinceId = await MaxAuditIdAsync();
            var currentClinic = new TestClinicContext(null);

            await using var context = _fixture.CreateContext(currentClinic);

            new AuditService(context, currentClinic, AnonymousAccessor())
                .Log("clinic.deactivated", "Clinic", 77);

            var row = Assert.Single(await NewRowsAsync(sinceId));

            Assert.Null(row.ClinicId);
            Assert.Null(row.UserId);
            Assert.Equal("clinic.deactivated", row.Action);
            Assert.Equal("Clinic", row.Entity);
            Assert.Equal(77, row.EntityId);
        }

        // =========================================================
        // Test 3: НИКАКЪВ PII — след login.failed никоя string колона
        // не съдържа имейла/паролата на потребителя
        // =========================================================

        [Fact]
        public async Task LoginFailedRows_NeverContainPii()
        {
            var email = $"pii-{Guid.NewGuid():N}@test.bg";
            var password = "WrongPass1";
            await SeedClinicAndUserAsync(email, IdentityTestHarness.TestPassword);

            var sinceId = await MaxAuditIdAsync();

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);
            await harness.NewController().Login(new LoginViewModel { Email = email, Password = password });

            var rows = await NewRowsAsync(sinceId);
            var failedRow = Assert.Single(rows, r => r.Action == "login.failed");

            // Проверка на ВСИЧКИ string колони — имейлът (и уникалният му локал), и
            // паролата не присъстват НИКЪДЕ. Int колоните не могат да носят текст.
            var localPart = email[..email.IndexOf('@')];
            foreach (var value in new[] { failedRow.Action, failedRow.Entity, failedRow.UserId })
            {
                Assert.DoesNotContain(email, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(localPart, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(password, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        // =========================================================
        // Test 4: login поток — 5 грешни опита → 5 login.failed + 1 login.locked;
        // опит върху заключен акаунт → само login.locked; успех → login.success
        // =========================================================

        [Fact]
        public async Task Login_FiveWrongAttempts_FiveFailedRowsPlusLockoutRow()
        {
            var email = $"lock-audit-{Guid.NewGuid():N}@test.bg";
            await SeedClinicAndUserAsync(email, IdentityTestHarness.TestPassword);

            var sinceId = await MaxAuditIdAsync();

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);

            for (var attempt = 1; attempt <= 5; attempt++)
            {
                await harness.NewController().Login(new LoginViewModel
                {
                    Email = email,
                    Password = "WrongPass1"
                });
            }

            var rows = await NewRowsAsync(sinceId);

            Assert.Equal(5, rows.Count(r => r.Action == "login.failed"));
            Assert.Equal(1, rows.Count(r => r.Action == "login.locked"));
            // Последният записан ред е заключването, непосредствено след последния провал.
            Assert.Equal("login.failed", rows[^2].Action);
            Assert.Equal("login.locked", rows[^1].Action);

            // Редовете от login-а носят САМО Entity "User" — entityId null (имейлът НИКОГА
            // не влиза; UserId е null: по време на login principal-ът е още анонимен).
            Assert.All(rows, r =>
            {
                Assert.Equal("User", r.Entity);
                Assert.Null(r.EntityId);
                Assert.Null(r.UserId);
            });

            // Опит с ВЯРНА парола върху вече заключен акаунт → САМО login.locked
            // (без фалшив login.failed — паролата не беше провалена, акаунтът е заключен).
            await harness.NewController().Login(new LoginViewModel
            {
                Email = email,
                Password = IdentityTestHarness.TestPassword
            });

            var afterRefusal = await NewRowsAsync(sinceId);
            Assert.Equal(5, afterRefusal.Count(r => r.Action == "login.failed"));
            Assert.Equal(2, afterRefusal.Count(r => r.Action == "login.locked"));

            // Успешен вход на втора (незаключена) сметка → login.success.
            var okEmail = $"ok-audit-{Guid.NewGuid():N}@test.bg";
            await SeedClinicAndUserAsync(okEmail, IdentityTestHarness.TestPassword);

            var sinceSecond = await MaxAuditIdAsync();
            await harness.NewController().Login(new LoginViewModel
            {
                Email = okEmail,
                Password = IdentityTestHarness.TestPassword
            });

            var successRow = Assert.Single(await NewRowsAsync(sinceSecond));
            Assert.Equal("login.success", successRow.Action);
            Assert.Equal("User", successRow.Entity);
        }

        // =========================================================
        // Test 5: patient.created / patient.deleted през PatientsController
        // с entityId == новия/изтрития пациент
        // =========================================================

        [Fact]
        public async Task PatientsController_CreateAndDelete_WriteAuditRowsWithEntityId()
        {
            var (clinicId, ownerId) = await SeedClinicAndOwnerAsync();

            var currentClinic = new TestClinicContext(clinicId);
            await using var context = _fixture.CreateContext(currentClinic);
            var controller = new PatientsController(
                new PatientService(context, currentClinic),
                new AuditService(context, currentClinic, AccessorWithUser("staff-user-1")));

            var sinceId = await MaxAuditIdAsync();

            var createResult = controller.Create(new PatientInputModel
            {
                Name = "Рекс Одит",
                Species = "Куче",
                Breed = "Дворнюк",
                Age = 3,
                OwnerId = ownerId
            });
            Assert.IsType<RedirectToActionResult>(createResult);

            // Новият PatientId — през ТЕНАНТ контекст (Patients са query-филтрирани;
            // raw null контекст би върнал нула реда — deny-by-default важи за tenant данни).
            int patientId;
            await using (var tenantRead = _fixture.CreateContext(currentClinic))
            {
                var patient = await tenantRead.Patients
                    .AsNoTracking()
                    .SingleAsync(p => p.OwnerId == ownerId && p.Name == "Рекс Одит");
                patientId = patient.PatientId;
            }

            var createRows = await NewRowsAsync(sinceId);
            var createdRow = Assert.Single(createRows);
            Assert.Equal("patient.created", createdRow.Action);
            Assert.Equal("Patient", createdRow.Entity);
            Assert.Equal(patientId, createdRow.EntityId);
            Assert.Equal(clinicId, createdRow.ClinicId);
            Assert.Equal("staff-user-1", createdRow.UserId);

            var sinceDelete = await MaxAuditIdAsync();
            var deleteResult = controller.DeleteConfirmed(patientId);
            Assert.IsType<RedirectToActionResult>(deleteResult);

            var deleteRow = Assert.Single(await NewRowsAsync(sinceDelete));
            Assert.Equal("patient.deleted", deleteRow.Action);
            Assert.Equal("Patient", deleteRow.Entity);
            Assert.Equal(patientId, deleteRow.EntityId);
        }

        // =========================================================
        // Test 5a: РЕАЛНИЯТ UI поток — POST /Home/Create (бутонът "Нов пациент"
        // и формата в Patients/Create постват към Home, не към Patients)
        // =========================================================

        [Fact]
        public async Task HomeController_CreateSuccess_WritesPatientCreatedRow()
        {
            var (clinicId, ownerId) = await SeedClinicAndOwnerAsync();
            var currentClinic = new TestClinicContext(clinicId);

            await using var context = _fixture.CreateContext(currentClinic);
            var controller = new HomeController(
                NullLogger<HomeController>.Instance,
                new PatientService(context, currentClinic),
                context,
                new AuditService(context, currentClinic, AccessorWithUser("staff-user-2")));

            var sinceId = await MaxAuditIdAsync();

            var result = controller.Create(new PatientInputModel
            {
                Name = "Макс Home",
                Species = "Куче",
                Breed = "Лабrador",
                Age = 2,
                OwnerId = ownerId
            });
            Assert.IsType<RedirectToActionResult>(result);

            int patientId;
            await using (var tenantRead = _fixture.CreateContext(currentClinic))
            {
                var patient = await tenantRead.Patients
                    .AsNoTracking()
                    .SingleAsync(p => p.OwnerId == ownerId && p.Name == "Макс Home");
                patientId = patient.PatientId;
            }

            var row = Assert.Single(await NewRowsAsync(sinceId));
            Assert.Equal("patient.created", row.Action);
            Assert.Equal("Patient", row.Entity);
            Assert.Equal(patientId, row.EntityId);
            Assert.Equal(clinicId, row.ClinicId);
            Assert.Equal("staff-user-2", row.UserId);
        }

        [Fact]
        public async Task HomeController_CreateInvalidModel_WritesNoAuditRow()
        {
            var (clinicId, ownerId) = await SeedClinicAndOwnerAsync();
            var currentClinic = new TestClinicContext(clinicId);

            await using var context = _fixture.CreateContext(currentClinic);
            var controller = new HomeController(
                NullLogger<HomeController>.Instance,
                new PatientService(context, currentClinic),
                context,
                new AuditService(context, currentClinic, AccessorWithUser("staff-user-2")));
            controller.ModelState.AddModelError(nameof(PatientInputModel.Name), "Задължително поле");

            var sinceId = await MaxAuditIdAsync();

            var result = controller.Create(new PatientInputModel { Name = "", OwnerId = ownerId });

            Assert.IsType<ViewResult>(result); // връща формата, без запис
            Assert.Empty(await NewRowsAsync(sinceId)); // одит САМО при успех
        }

        // =========================================================
        // Test 5b: ClinicAdminController — staff.created + посоката на toggle
        // (регресия: посоката се снема ПРЕДИ мутацията на tracked instance-а)
        // =========================================================

        [Fact]
        public async Task ClinicAdminController_StaffCreatedAndToggle_WritesDirectionalRows()
        {
            var clinicId = await SeedClinicAsync();

            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);
            var controller = new ClinicAdminController(
                harness.StaffService,
                harness.UserManager,
                new AuditService(
                    harness.Context,
                    new TestClinicContext(clinicId),
                    AccessorWithUser("admin-user-1")));

            // Bare контролер (без MVC pipeline) — TempData идва от реален provider,
            // за да не гърми CreateStaff на TempData[...] (действието не се променя).
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new DefaultHttpContext(),
                new NullTempDataProvider());

            var email = $"audit-staff-{Guid.NewGuid():N}@test.bg";
            var sinceCreate = await MaxAuditIdAsync();

            var createResult = await controller.CreateStaff(new CreateStaffInputModel
            {
                Email = email,
                DisplayName = "Служител Одит",
                Password = StaffTestHarness.TestPassword,
                Role = AppRoles.Receptionist
            });
            Assert.IsType<RedirectToActionResult>(createResult);

            var createdRow = Assert.Single(await NewRowsAsync(sinceCreate));
            Assert.Equal("staff.created", createdRow.Action);
            Assert.Equal("Staff", createdRow.Entity);
            Assert.Null(createdRow.EntityId); // субектът (PII) не се записва
            Assert.Equal(clinicId, createdRow.ClinicId);
            Assert.Equal("admin-user-1", createdRow.UserId);

            var staff = await harness.UserManager.FindByEmailAsync(email);
            Assert.NotNull(staff);

            // Активен → toggle → ДЕАКТИВИРАН (посоката се чете преди промяната).
            var sinceDeactivate = await MaxAuditIdAsync();
            await controller.ToggleActive(staff!.Id);
            var deactivatedRow = Assert.Single(await NewRowsAsync(sinceDeactivate));
            Assert.Equal("staff.deactivated", deactivatedRow.Action);
            Assert.Equal("Staff", deactivatedRow.Entity);
            Assert.Null(deactivatedRow.EntityId);

            // Деактивиран → toggle → АКТИВИРАН.
            var sinceActivate = await MaxAuditIdAsync();
            await controller.ToggleActive(staff.Id);
            var activatedRow = Assert.Single(await NewRowsAsync(sinceActivate));
            Assert.Equal("staff.activated", activatedRow.Action);
        }

        // =========================================================
        // Test 5c: чужд клиникен userId → SetStaffActive=false и НИКАКЪВ одитен ред
        // (FindByIdAsync вижда всички клиники; одитът следва реалния резултат)
        // =========================================================

        [Fact]
        public async Task ClinicAdminController_ForeignClinicToggle_ReturnsFalseAndWritesNoAuditRow()
        {
            var clinicAId = await SeedClinicAsync();
            var clinicBId = await SeedClinicAsync();

            await using var harnessA = await StaffTestHarness.BuildAsync(_fixture, clinicAId);
            var email = $"foreign-{Guid.NewGuid():N}@test.bg";
            Assert.True(harnessA.StaffService
                .CreateStaff(email, "Чужд Служител", StaffTestHarness.TestPassword, AppRoles.Receptionist)
                .Succeeded);
            var userA = await harnessA.UserManager.FindByEmailAsync(email);
            Assert.NotNull(userA);

            // Контролер на клиника Б с userId от клиника А: FindByIdAsync го намира
            // (Identity таблиците не са tenant-филтрирани), но SetStaffActive е no-op.
            await using var harnessB = await StaffTestHarness.BuildAsync(_fixture, clinicBId);
            var controllerB = new ClinicAdminController(
                harnessB.StaffService,
                harnessB.UserManager,
                new AuditService(
                    harnessB.Context,
                    new TestClinicContext(clinicBId),
                    AccessorWithUser("admin-b")));

            var sinceId = await MaxAuditIdAsync();
            var result = await controllerB.ToggleActive(userA!.Id);
            Assert.IsType<RedirectToActionResult>(result);

            // НИКАКЪВ одитен ред — действието не се е случило (не фалшив staff.* запис).
            Assert.Empty(await NewRowsAsync(sinceId));
            Assert.True((await harnessA.UserManager.FindByEmailAsync(email))!.IsActive);

            // Bool договорът: чужд контекст → false; собствен → true (реално записан).
            Assert.False(harnessB.StaffService.SetStaffActive(userA.Id, active: false));
            Assert.True(harnessA.StaffService.SetStaffActive(userA.Id, active: false));
            Assert.False((await harnessA.UserManager.FindByEmailAsync(email))!.IsActive);
        }

        // =========================================================
        // Test 6: action > 50 знака → ТРУНКВАНЕ до 50, без изключение
        // =========================================================

        [Fact]
        public async Task Log_ActionOver50Chars_TruncatedWithoutException()
        {
            var sinceId = await MaxAuditIdAsync();
            var currentClinic = new TestClinicContext(10);

            await using var context = _fixture.CreateContext(currentClinic);

            new AuditService(context, currentClinic, AnonymousAccessor())
                .Log(new string('x', 80), "Patient", 1);

            var row = Assert.Single(await NewRowsAsync(sinceId));
            Assert.Equal(50, row.Action.Length);
            Assert.Equal(new string('x', 50), row.Action);
        }

        // =========================================================
        // Test 6b: празен/whitespace action → fail-fast (ArgumentException)
        // =========================================================

        [Fact]
        public async Task Log_EmptyAction_ThrowsArgumentException()
        {
            var currentClinic = new TestClinicContext(10);
            await using var context = _fixture.CreateContext(currentClinic);
            var audit = new AuditService(context, currentClinic, AnonymousAccessor());

            Assert.Throws<ArgumentException>(() => audit.Log(""));
            Assert.Throws<ArgumentException>(() => audit.Log("   "));
            Assert.Throws<ArgumentException>(() => audit.Log(null!));
        }

        // =========================================================
        // Test 7: AuditLog НЕ е tenant филтриран — и raw (null), и tenant
        // контекст четат ВСИЧКИ редове (вкл. ClinicId NULL и чужда клиника)
        // =========================================================

        [Fact]
        public async Task AuditLog_NotTenantFiltered_ReadableFromRawAndTenantContexts()
        {
            var sinceId = await MaxAuditIdAsync();
            var currentClinic = new TestClinicContext(10);

            // 1 ред от клиника 10 + 1 операторски ред (ClinicId NULL).
            await using (var tenantWrite = _fixture.CreateContext(currentClinic))
            {
                new AuditService(tenantWrite, currentClinic, AnonymousAccessor())
                    .Log("patient.created", "Patient", 11);
            }

            await using (var operatorWrite = _fixture.CreateContext(new TestClinicContext(null)))
            {
                new AuditService(operatorWrite, new TestClinicContext(null), AnonymousAccessor())
                    .Log("clinic.activated", "Clinic", 10);
            }

            // Raw контекст (TestClinicContext(null)) ВИЖДА и двата реда — zero-row
            // denial-ът на tenant таблиците НЕ се отнася за одита.
            await using var rawRead = _fixture.CreateContext(new TestClinicContext(null));
            Assert.Equal(2, await rawRead.AuditLogs.CountAsync(a => a.AuditId > sinceId));

            // И tenant контекстът (10) също вижда ВСИЧКИ редове — нямa query filter
            // върху AuditLog (той не е tenant entity; не съществува и tenant READ API
            // — само суров контекст/IAuditService за писане).
            await using var tenantRead = _fixture.CreateContext(currentClinic);
            Assert.Equal(2, await tenantRead.AuditLogs.CountAsync(a => a.AuditId > sinceId));
        }

        // =========================================================
        // Помощни методи
        // =========================================================

        /// <summary>Дължината на одитната таблица преди теста — тестовете споделят ЕДНА
        /// ephemeral база, затова новите редове се откриват по AuditId (монотонно identity).</summary>
        private async Task<long> MaxAuditIdAsync()
        {
            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            return await raw.AuditLogs.AnyAsync() ? await raw.AuditLogs.MaxAsync(a => a.AuditId) : 0;
        }

        private async Task<List<AuditLog>> NewRowsAsync(long sinceId)
        {
            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            return await raw.AuditLogs
                .AsNoTracking()
                .Where(a => a.AuditId > sinceId)
                .OrderBy(a => a.AuditId)
                .ToListAsync();
        }

        /// <summary>Активна клиника (суров seed, без tenant контекст) → ClinicId.</summary>
        private async Task<int> SeedClinicAsync()
        {
            await using var seed = _fixture.CreateContext(new TestClinicContext(null));
            var clinic = new Clinic
            {
                Name = $"Одит-клиника {Guid.NewGuid():N}".Substring(0, 30),
                CreatedAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            seed.Clinics.Add(clinic);
            await seed.SaveChangesAsync();
            return clinic.ClinicId;
        }

        /// <summary>Клиника + стопанин (за patient пътищата).</summary>
        private async Task<(int ClinicId, int OwnerId)> SeedClinicAndOwnerAsync()
        {
            var clinicId = await SeedClinicAsync();

            await using var seed = _fixture.CreateContext(new TestClinicContext(null));
            var owner = new Owner { FullName = "Стопанин Одит", ClinicId = clinicId };
            seed.Owners.Add(owner);
            await seed.SaveChangesAsync();

            return (clinicId, owner.OwnerId);
        }

        private async Task SeedClinicAndUserAsync(string email, string password)
        {
            int clinicId;
            await using (var seed = _fixture.CreateContext(new TestClinicContext(null)))
            {
                var clinic = new Clinic
                {
                    Name = $"Одит-user {Guid.NewGuid():N}".Substring(0, 30),
                    CreatedAtUtc = DateTime.UtcNow,
                    IsActive = true
                };
                seed.Clinics.Add(clinic);
                await seed.SaveChangesAsync();
                clinicId = clinic.ClinicId;
            }

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);
            var userManager = harness.Provider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = "Д-р Одит",
                ClinicId = clinicId,
                IsActive = true
            };
            var createResult = await userManager.CreateAsync(user, password);
            Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        /// <summary>Accessor с аутентикиран principal (NameIdentifier claim) — плоско
        /// инстанционен (същият модел като IdentityTestHarness.FixedHttpContextAccessor,
        /// тъй като истинският IHttpContextAccessor ползва AsyncLocal).</summary>
        private static IHttpContextAccessor AccessorWithUser(string userId)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, authenticationType: "Test"))
            };
            return new FixedHttpContextAccessor { HttpContext = httpContext };
        }

        private static IHttpContextAccessor AnonymousAccessor() =>
            new FixedHttpContextAccessor { HttpContext = new DefaultHttpContext() };

        private sealed class FixedHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext? HttpContext { get; set; }
        }

        private sealed class NullTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
        {
            public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

            public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
            {
            }
        }
    }
}
