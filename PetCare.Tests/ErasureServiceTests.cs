using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
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
    /// GDPR erasure (Task 11, design doc §7): ErasureService изтрива ВСИЧКИ tenant данни на
    /// клиниката в ЕДНА транзакция — ваксинации → медицински записи → записвания → пациенти
    /// → стопани → ветеринарни профили → потребители + Identity join редове → Clinic реда.
    /// Одитът НЕ се трие: `clinic.deleted` се записва ВЪТРЕ в транзакцията (fail-closed), а
    /// стари одитни редове на клиниката остават след изтриването (няма FK).
    ///
    /// Тестовете доказват и идемпотентността (повторно извикване = no-op, БЕЗ втори одитен
    /// ред) и че контролерът изисква точно въведено име преди разрушителното действие.
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class ErasureServiceTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public ErasureServiceTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        // =========================================================
        // Test 1: пълен fan-out + контролна клиника + одит
        // =========================================================

        [Fact]
        public async Task EraseClinic_RemovesClinicADataUsersAndClinic_KeepsClinicBAndAuditRow()
        {
            var seed = await SeedFullScenarioAsync();

            var before = await CountClinicDataAsync(seed.ClinicAId);
            Assert.Equal(new ClinicCounts(2, 2, 2, 3, 2, 1), before);

            Erase(seed.ClinicAId, "erasure-actor");

            // Всички шест tenant таблици — нула реда за клиника А.
            Assert.Equal(new ClinicCounts(0, 0, 0, 0, 0, 0), await CountClinicDataAsync(seed.ClinicAId));

            await using var raw = CreateRawContext();

            // Clinic редът е изчезнал; потребителите на А — също.
            Assert.False(await raw.Clinics.AnyAsync(c => c.ClinicId == seed.ClinicAId));
            Assert.Equal(0, await raw.Users.CountAsync(u => u.ClinicId == seed.ClinicAId));

            // Identity join редовете за ТЕЗИ потребители са изтрити.
            var userIds = new[] { seed.AdminUserId, seed.VetUserId };
            Assert.Equal(0, await raw.UserRoles.CountAsync(r => userIds.Contains(r.UserId)));
            Assert.Equal(0, await raw.UserClaims.CountAsync(c => userIds.Contains(c.UserId)));
            Assert.Equal(0, await raw.UserLogins.CountAsync(l => userIds.Contains(l.UserId)));
            Assert.Equal(0, await raw.UserTokens.CountAsync(t => userIds.Contains(t.UserId)));

            // Контролната клиника Б е непокътната (по 1 ред от всеки вид).
            Assert.Equal(new ClinicCounts(1, 1, 1, 1, 1, 1), await CountClinicDataAsync(seed.ClinicBId));
            Assert.True(await raw.Clinics.AnyAsync(c => c.ClinicId == seed.ClinicBId));
            Assert.Equal(1, await raw.Users.CountAsync(u => u.ClinicId == seed.ClinicBId));

            // Одит: clinic.deleted остава, с ClinicId/EntityId == А и САМО кодове/Id-та.
            var auditRows = await raw.AuditLogs
                .Where(a => a.ClinicId == seed.ClinicAId)
                .ToListAsync();

            var deletedRow = Assert.Single(auditRows, a => a.Action == "clinic.deleted");
            Assert.Equal("Clinic", deletedRow.Entity);
            Assert.Equal(seed.ClinicAId, deletedRow.EntityId);
            Assert.Equal("erasure-actor", deletedRow.UserId);

            // Допълнителният одитен ред от seed-а също оцелява (одитът не се трие).
            Assert.Contains(auditRows, a => a.Action == "patient.created" && a.Entity == "Patient");

            // Никакво PII в одита — нито име на клиника, нито имейл/показно име на потребител.
            var forbidden = new[]
            {
                seed.ClinicAName,
                seed.AdminEmail,
                seed.AdminDisplayName,
                seed.VetEmail,
                seed.VetDisplayName
            };

            Assert.All(auditRows, row =>
            {
                foreach (var value in new[] { row.Action, row.Entity, row.UserId })
                {
                    foreach (var needle in forbidden)
                    {
                        Assert.DoesNotContain(needle, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    }
                }
            });
        }

        // =========================================================
        // Test 2: повторно извикване = no-op (без изключение, БЕЗ втори одитен ред)
        // =========================================================

        [Fact]
        public async Task EraseClinic_SecondCall_IsNoOpWithoutNewAuditRow()
        {
            var seed = await SeedFullScenarioAsync();

            Erase(seed.ClinicAId, "erasure-actor");

            var afterFirst = await CountClinicDeletedRowsAsync(seed.ClinicAId);
            Assert.Equal(1, afterFirst);

            // Повторно извикване върху СВЕЖ контекст — клиниката вече не съществува.
            Erase(seed.ClinicAId, "erasure-actor");

            Assert.Equal(1, await CountClinicDeletedRowsAsync(seed.ClinicAId));
        }

        // =========================================================
        // Test 3: одитните редове на клиниката ОЦЕЛЯВАТ (без FK към Clinics)
        // =========================================================

        [Fact]
        public async Task EraseClinic_KeepsAuditRowsForErasedClinic()
        {
            var suffix = UniqueSuffix();
            int clinicId;

            await using (var seed = CreateRawContext())
            {
                var clinic = new Clinic
                {
                    Name = $"Одит-оцеляване {suffix}",
                    CreatedAtUtc = DateTime.UtcNow,
                    IsActive = true
                };
                seed.Clinics.Add(clinic);
                await seed.SaveChangesAsync();
                clinicId = clinic.ClinicId;

                seed.AuditLogs.Add(new AuditLog
                {
                    ClinicId = clinicId,
                    Action = "patient.deleted",
                    Entity = "Patient",
                    EntityId = 5,
                    AtUtc = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }

            Erase(clinicId, "erasure-actor");

            await using var raw = CreateRawContext();
            Assert.False(await raw.Clinics.AnyAsync(c => c.ClinicId == clinicId));

            var rows = await raw.AuditLogs.Where(a => a.ClinicId == clinicId).ToListAsync();
            Assert.Contains(rows, r => r.Action == "patient.deleted" && r.EntityId == 5);
            Assert.Contains(rows, r => r.Action == "clinic.deleted" && r.EntityId == clinicId);
            Assert.Equal(2, rows.Count);
        }

        // =========================================================
        // Test 4: несъществуваща клиника → no-op, без одитен ред
        // =========================================================

        [Fact]
        public async Task EraseClinic_UnknownClinicId_IsNoOp()
        {
            var unknownId = 991_000_000 + Random.Shared.Next(1, 9_999);

            Erase(unknownId, "erasure-actor");

            await using var raw = CreateRawContext();
            Assert.False(await raw.Clinics.AnyAsync(c => c.ClinicId == unknownId));
            Assert.False(await raw.AuditLogs.AnyAsync(a =>
                a.Action == "clinic.deleted" && a.EntityId == unknownId));
        }

        // =========================================================
        // Test 5: DeleteClinic UI — грешно въведено име не изтрива нищо
        // =========================================================

        [Fact]
        public async Task ClinicAdminDeleteClinic_WrongTypedName_DoesNotErase()
        {
            var (clinicId, clinicName) = await SeedEmptyClinicAsync();
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);
            var auth = new RecordingAuthenticationService();

            var controller = BuildDeleteClinicController(harness, clinicId, auth);

            var result = await controller.DeleteClinicConfirmed("Грешно име");

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<DeleteClinicViewModel>(view.Model);
            Assert.Equal(clinicName, model.ClinicName);
            Assert.False(controller.ModelState.IsValid);
            Assert.False(auth.SignedOut);

            await using var raw = CreateRawContext();
            Assert.True(await raw.Clinics.AnyAsync(c => c.ClinicId == clinicId));
            Assert.False(await raw.AuditLogs.AnyAsync(a =>
                a.Action == "clinic.deleted" && a.EntityId == clinicId));
        }

        // =========================================================
        // Test 6: DeleteClinic UI — точно име → изтрива, SignOut, redirect към Locked
        // =========================================================

        [Fact]
        public async Task ClinicAdminDeleteClinic_ExactName_ErasesSignsOutAndRedirectsToLocked()
        {
            var (clinicId, clinicName) = await SeedEmptyClinicAsync();
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);
            var auth = new RecordingAuthenticationService();

            var controller = BuildDeleteClinicController(harness, clinicId, auth);

            var result = await controller.DeleteClinicConfirmed(clinicName);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Locked", redirect.ActionName);
            Assert.Equal("Account", redirect.ControllerName);
            Assert.True(auth.SignedOut);

            await using var raw = CreateRawContext();
            Assert.False(await raw.Clinics.AnyAsync(c => c.ClinicId == clinicId));
            Assert.True(await raw.AuditLogs.AnyAsync(a =>
                a.Action == "clinic.deleted" && a.EntityId == clinicId));
        }

        // =========================================================
        // Помощни методи
        // =========================================================

        private void Erase(int clinicId, string actorUserId)
        {
            var currentClinic = new TestClinicContext(clinicId);
            using var context = _fixture.CreateContext(currentClinic);

            new ErasureService(
                    context,
                    currentClinic,
                    ActorAccessor(actorUserId),
                    NullLogger<ErasureService>.Instance)
                .EraseClinic(clinicId);
        }

        private ClinicAdminController BuildDeleteClinicController(
            StaffTestHarness.Harness harness,
            int clinicId,
            RecordingAuthenticationService auth)
        {
            var currentClinic = new TestClinicContext(clinicId);
            var audit = new AuditService(harness.Context, currentClinic, AnonymousAccessor());
            var erasure = new ErasureService(
                harness.Context,
                currentClinic,
                ActorAccessor("admin-user"),
                NullLogger<ErasureService>.Instance);

            var authServicesCollection = new ServiceCollection();
            authServicesCollection.AddSingleton<IAuthenticationService>(auth);
            authServicesCollection.AddControllersWithViews();
            var authServices = authServicesCollection.BuildServiceProvider();

            return new ClinicAdminController(
                harness.StaffService,
                harness.UserManager,
                audit,
                erasure,
                currentClinic,
                harness.Context,
                NullLogger<ClinicAdminController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { RequestServices = authServices },
                    RouteData = new RouteData(),
                    ActionDescriptor = new ControllerActionDescriptor
                    {
                        ControllerName = "ClinicAdmin",
                        ActionName = "DeleteClinic"
                    }
                }
            };
        }

        private async Task<int> CountClinicDeletedRowsAsync(int clinicId)
        {
            await using var raw = CreateRawContext();
            return await raw.AuditLogs.CountAsync(a =>
                a.Action == "clinic.deleted" && a.EntityId == clinicId);
        }

        private async Task<ClinicCounts> CountClinicDataAsync(int clinicId)
        {
            await using var raw = CreateRawContext();

            return new ClinicCounts(
                await raw.Vaccinations.IgnoreQueryFilters().CountAsync(e => e.ClinicId == clinicId),
                await raw.MedicalRecords.IgnoreQueryFilters().CountAsync(e => e.ClinicId == clinicId),
                await raw.Appointments.IgnoreQueryFilters().CountAsync(e => e.ClinicId == clinicId),
                await raw.Patients.IgnoreQueryFilters().CountAsync(e => e.ClinicId == clinicId),
                await raw.Owners.IgnoreQueryFilters().CountAsync(e => e.ClinicId == clinicId),
                await raw.Veterinarians.IgnoreQueryFilters().CountAsync(e => e.ClinicId == clinicId));
        }

        private VetClinicContext CreateRawContext() =>
            _fixture.CreateContext(new TestClinicContext(null));

        /// <summary>
        /// Клиника А с 2 стопани, 3 пациенти, 2 записвания, 2 мед. записа, 2 ваксинации,
        /// 1 ветеринарен профил, 2 потребителя (+ role/claim/login/token редове) и клиника Б
        /// със същия обем данни като контрола.
        /// </summary>
        private async Task<ErasureSeed> SeedFullScenarioAsync()
        {
            var suffix = UniqueSuffix();
            var clinicAName = $"Клиника за изтриване {suffix}";
            var adminEmail = $"erase-admin-{suffix}@test.bg";
            var adminDisplayName = $"Админ Изтривалков {suffix}";
            var vetEmail = $"erase-vet-{suffix}@test.bg";
            var vetDisplayName = $"Д-р Изтривалков {suffix}";

            int clinicAId, clinicBId, ownerAId, vetAId;
            var adminUserId = Guid.NewGuid().ToString("N");
            var vetUserId = Guid.NewGuid().ToString("N");

            await using (var seed = CreateRawContext())
            {
                var clinicA = new Clinic { Name = clinicAName, CreatedAtUtc = DateTime.UtcNow, IsActive = true };
                var clinicB = new Clinic { Name = $"Контролна клиника {suffix}", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
                seed.Clinics.AddRange(clinicA, clinicB);
                await seed.SaveChangesAsync();

                clinicAId = clinicA.ClinicId;
                clinicBId = clinicB.ClinicId;

                var ownerA1 = new Owner { FullName = $"Стопанин А1 {suffix}", ClinicId = clinicAId };
                var ownerA2 = new Owner { FullName = $"Стопанин А2 {suffix}", ClinicId = clinicAId };
                var ownerB = new Owner { FullName = $"Стопанин Б {suffix}", ClinicId = clinicBId };
                seed.Owners.AddRange(ownerA1, ownerA2, ownerB);

                var vetA = new Veterinarian { FullName = $"Д-р А {suffix}", ClinicId = clinicAId };
                var vetB = new Veterinarian { FullName = $"Д-р Б {suffix}", ClinicId = clinicBId };
                seed.Veterinarians.AddRange(vetA, vetB);
                await seed.SaveChangesAsync();

                ownerAId = ownerA1.OwnerId;
                vetAId = vetA.VetId;

                var patientA1 = new Patient { Name = $"Пациент А1 {suffix}", Species = "Куче", OwnerId = ownerA1.OwnerId, ClinicId = clinicAId };
                var patientA2 = new Patient { Name = $"Пациент А2 {suffix}", Species = "Котка", OwnerId = ownerA1.OwnerId, ClinicId = clinicAId };
                var patientA3 = new Patient { Name = $"Пациент А3 {suffix}", Species = "Куче", OwnerId = ownerA2.OwnerId, ClinicId = clinicAId };
                var patientB = new Patient { Name = $"Пациент Б {suffix}", Species = "Куче", OwnerId = ownerB.OwnerId, ClinicId = clinicBId };
                seed.Patients.AddRange(patientA1, patientA2, patientA3, patientB);
                await seed.SaveChangesAsync();

                var date = DateOnly.FromDateTime(DateTime.UtcNow);
                seed.Appointments.AddRange(
                    new Appointment { PatientId = patientA1.PatientId, VetId = vetA.VetId, ClinicId = clinicAId, AppointmentDate = date, AppointmentTime = new TimeOnly(9, 0), DurationMinutes = 30 },
                    new Appointment { PatientId = patientA2.PatientId, VetId = vetA.VetId, ClinicId = clinicAId, AppointmentDate = date, AppointmentTime = new TimeOnly(10, 0), DurationMinutes = 30 },
                    new Appointment { PatientId = patientB.PatientId, VetId = vetB.VetId, ClinicId = clinicBId, AppointmentDate = date, AppointmentTime = new TimeOnly(11, 0), DurationMinutes = 30 });

                seed.MedicalRecords.AddRange(
                    new MedicalRecord { PatientId = patientA1.PatientId, VetId = vetA.VetId, ClinicId = clinicAId, Diagnosis = "Диагноза А1" },
                    new MedicalRecord { PatientId = patientA2.PatientId, VetId = vetA.VetId, ClinicId = clinicAId, Diagnosis = "Диагноза А2" },
                    new MedicalRecord { PatientId = patientB.PatientId, VetId = vetB.VetId, ClinicId = clinicBId, Diagnosis = "Диагноза Б" });

                seed.Vaccinations.AddRange(
                    new Vaccination { PatientId = patientA1.PatientId, VetId = vetA.VetId, ClinicId = clinicAId, VaccineName = $"Ваксина А1 {suffix}", DateAdministered = date },
                    new Vaccination { PatientId = patientA2.PatientId, VetId = vetA.VetId, ClinicId = clinicAId, VaccineName = $"Ваксина А2 {suffix}", DateAdministered = date },
                    new Vaccination { PatientId = patientB.PatientId, VetId = vetB.VetId, ClinicId = clinicBId, VaccineName = $"Ваксина Б {suffix}", DateAdministered = date });

                // Допълнителен одитен ред за А — трябва да оцелее след изтриването.
                seed.AuditLogs.Add(new AuditLog
                {
                    ClinicId = clinicAId,
                    Action = "patient.created",
                    Entity = "Patient",
                    EntityId = patientA1.PatientId,
                    AtUtc = DateTime.UtcNow
                });

                await seed.SaveChangesAsync();
            }

            // Потребители (+ Identity join редове) — суров seed, без UserManager: тестът
            // проверява fan-out-а, не пароли/хеширане.
            await using (var seed = CreateRawContext())
            {
                var roles = await EnsureRolesAsync(seed, AppRoles.ClinicAdmin, AppRoles.Vet);

                seed.Users.AddRange(
                    NewUser(adminUserId, adminEmail, adminDisplayName, clinicAId, veterinarianId: null),
                    NewUser(vetUserId, vetEmail, vetDisplayName, clinicAId, veterinarianId: vetAId),
                    NewUser(Guid.NewGuid().ToString("N"), $"erase-control-{suffix}@test.bg", $"Контролен {suffix}", clinicBId, veterinarianId: null));

                await seed.SaveChangesAsync();

                seed.UserRoles.AddRange(
                    new IdentityUserRole<string> { UserId = adminUserId, RoleId = roles[AppRoles.ClinicAdmin].Id },
                    new IdentityUserRole<string> { UserId = vetUserId, RoleId = roles[AppRoles.Vet].Id });
                seed.UserClaims.Add(new IdentityUserClaim<string>
                {
                    UserId = adminUserId,
                    ClaimType = "test_claim",
                    ClaimValue = "test_value"
                });
                seed.UserLogins.Add(new IdentityUserLogin<string>
                {
                    LoginProvider = "TestProvider",
                    ProviderKey = $"key-{suffix}",
                    UserId = vetUserId,
                    ProviderDisplayName = "Test"
                });
                seed.UserTokens.Add(new IdentityUserToken<string>
                {
                    UserId = vetUserId,
                    LoginProvider = "TestProvider",
                    Name = "test_token",
                    Value = "test_value"
                });

                await seed.SaveChangesAsync();
            }

            return new ErasureSeed(
                clinicAId,
                clinicBId,
                clinicAName,
                ownerAId,
                vetAId,
                adminUserId,
                vetUserId,
                adminEmail,
                adminDisplayName,
                vetEmail,
                vetDisplayName);
        }

        private async Task<(int ClinicId, string ClinicName)> SeedEmptyClinicAsync()
        {
            var suffix = UniqueSuffix();
            var clinicName = $"Празна клиника {suffix}";

            await using var seed = CreateRawContext();
            var clinic = new Clinic { Name = clinicName, CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            seed.Clinics.Add(clinic);
            await seed.SaveChangesAsync();

            return (clinic.ClinicId, clinicName);
        }

        private static async Task<Dictionary<string, IdentityRole>> EnsureRolesAsync(
            VetClinicContext context,
            params string[] roleNames)
        {
            var result = new Dictionary<string, IdentityRole>();

            foreach (var roleName in roleNames)
            {
                var normalized = roleName.ToUpperInvariant();
                var role = await context.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalized);
                if (role == null)
                {
                    role = new IdentityRole(roleName) { NormalizedName = normalized };
                    context.Roles.Add(role);
                    await context.SaveChangesAsync();
                }

                result[roleName] = role;
            }

            return result;
        }

        private static ApplicationUser NewUser(
            string id,
            string email,
            string displayName,
            int clinicId,
            int? veterinarianId) => new()
            {
                Id = id,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                DisplayName = displayName,
                ClinicId = clinicId,
                VeterinarianId = veterinarianId,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString("N")
            };

        private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];

        private static IHttpContextAccessor ActorAccessor(string userId)
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

        private sealed class RecordingAuthenticationService : IAuthenticationService
        {
            public bool SignedOut { get; private set; }

            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
                Task.FromResult(AuthenticateResult.NoResult());

            public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            {
                SignedOut = true;
                return Task.CompletedTask;
            }
        }

        private sealed record ClinicCounts(
            int Vaccinations,
            int MedicalRecords,
            int Appointments,
            int Patients,
            int Owners,
            int Veterinarians);

        private sealed record ErasureSeed(
            int ClinicAId,
            int ClinicBId,
            string ClinicAName,
            int OwnerAId,
            int VetAId,
            string AdminUserId,
            string VetUserId,
            string AdminEmail,
            string AdminDisplayName,
            string VetEmail,
            string VetDisplayName);
    }
}
