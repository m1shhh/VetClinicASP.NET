using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Consent + публична регистрация (Task 7, design doc §7): съгласието се валидира
    /// СЪРВЪРНО (не само [Required]) — без отметка НИКАКЪВ Clinic/ApplicationUser ред;
    /// при успех Clinic носи ConsentGivenAtUtc/ConsentTextVersion="v1"/Trial; при
    /// затворен toggle → 404; при слаба парола транзакцията трие Clinic реда (атомарност).
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class ConsentAndRegistrationTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public ConsentAndRegistrationTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Register_WithoutConsent_AddsModelStateError_AndPersistsNothing()
        {
            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            var clinicsBefore = await raw.Clinics.CountAsync();

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: true);
            var controller = harness.Controller;

            var model = ValidModel(consentAccepted: false);
            var result = await controller.Register(model);

            // Сървърната проверка (НЕ [Required] атрибут) — грешката споменава "условия".
            var view = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            var error = Assert.Single(controller.ModelState[string.Empty]!.Errors);
            Assert.Contains("условия", error.ErrorMessage);

            // НИКАКЪВ Clinic ред, НИКАКЪВ user ред.
            var clinicsAfter = await raw.Clinics.CountAsync();
            Assert.Equal(clinicsBefore, clinicsAfter);
            Assert.Equal(0, await raw.Users.CountAsync(u => u.Email == model.Email));
        }

        [Fact]
        public async Task Register_WithConsent_CreatesClinicWithConsentFields_AndClinicAdminUser()
        {
            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            var clinicsBefore = await raw.Clinics.CountAsync();

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: true);
            var controller = harness.Controller;

            var model = ValidModel(consentAccepted: true);
            var result = await controller.Register(model);

            // Redirect Patients/Index (план, Task 7).
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Patients", redirect.ControllerName);

            // Clinic ред: consent timestamp + версия + Trial + активна.
            var clinic = Assert.Single(await raw.Clinics.Where(c => c.Name == model.ClinicName).ToListAsync());
            Assert.NotNull(clinic.ConsentGivenAtUtc);
            Assert.Equal("v1", clinic.ConsentTextVersion);
            Assert.Equal(ClinicBillingStatus.Trial, clinic.BillingStatus);
            Assert.True(clinic.IsActive);
            Assert.True(Math.Abs((clinic.ConsentGivenAtUtc!.Value - DateTime.UtcNow).TotalMinutes) < 2);

            // ApplicationUser ред: свързан към НОВОТО clinic id, активен, в роля ClinicAdmin.
            var userManager = harness.Provider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(model.Email);
            Assert.NotNull(user);
            Assert.Equal(clinic.ClinicId, user!.ClinicId);
            Assert.True(user.IsActive);
            Assert.Equal(model.DisplayName, user.DisplayName);
            Assert.True(await userManager.IsInRoleAsync(user, AppRoles.ClinicAdmin));

            // ЕДИН нов Clinic ред — регистрацията не създава нищо друго.
            var clinicsAfter = await raw.Clinics.CountAsync();
            Assert.Equal(clinicsBefore + 1, clinicsAfter);

            // Анонимният път пише clinic.registered с ClinicId NULL (регистрантът още
            // няма clinic_id claim) и EntityId = новия ClinicId (Task 12, част 4).
            var audit = Assert.Single(await raw.AuditLogs
                .Where(a => a.Action == "clinic.registered" && a.EntityId == clinic.ClinicId)
                .ToListAsync());
            Assert.Null(audit.ClinicId);
        }

        [Fact]
        public async Task Register_WhenAlreadyAuthenticated_IsRejected_PersistsNothing_NoAudit()
        {
            // Task 12, част 4 (разширение от Task 10 ревю): аутентикиран POST Register би
            // наследил стария clinic_id claim и би записал clinic.registered с грешна ClinicId.
            // Guard-ът връща redirect Home/Index и НЕ пипа базата.
            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            var clinicsBefore = await raw.Clinics.CountAsync();
            var registeredAuditsBefore = await raw.AuditLogs.CountAsync(a => a.Action == "clinic.registered");

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: true);

            harness.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "existing-user-id"),
                new Claim("clinic_id", "1")
            }, authenticationType: "Test"));

            var controller = harness.Controller;
            controller.ControllerContext = new ControllerContext { HttpContext = harness.HttpContext };

            var model = ValidModel(consentAccepted: true);
            var result = await controller.Register(model);

            // Избрано поведение: form + model error (не redirect) — виж AccountController.
            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(
                "анонимни",
                Assert.Single(controller.ModelState[string.Empty]!.Errors).ErrorMessage);

            // НИКАКЪВ нов Clinic/User ред и НИКАКЪВ нов одитен запис.
            Assert.Equal(clinicsBefore, await raw.Clinics.CountAsync());
            Assert.Equal(0, await raw.Users.CountAsync(u => u.Email == model.Email));
            Assert.Equal(registeredAuditsBefore, await raw.AuditLogs.CountAsync(a => a.Action == "clinic.registered"));
        }

        [Fact]
        public async Task Register_WhenToggleIsOff_ReturnsNotFound_AndPersistsNothing()
        {
            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            var clinicsBefore = await raw.Clinics.CountAsync();

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);
            var controller = harness.Controller;

            var model = ValidModel(consentAccepted: true);
            var postResult = await controller.Register(model);
            Assert.IsType<NotFoundResult>(postResult);

            var getResult = controller.Register();
            Assert.IsType<NotFoundResult>(getResult);

            Assert.Equal(clinicsBefore, await raw.Clinics.CountAsync());
            Assert.Equal(0, await raw.Users.CountAsync(u => u.Email == model.Email));
        }

        [Fact]
        public async Task Register_WithWeakPassword_RollsBackClinic_NoRowsRemain()
        {
            // "abcdefgh" минава VM валидацията (8+ символа) и ТРОГВА identity политиката
            // (без главна буква/цифра) — пътят Clinic INSERT → CreateAsync fail → rollback.
            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            var clinicsBefore = await raw.Clinics.CountAsync();

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: true);
            var controller = harness.Controller;

            var model = ValidModel(consentAccepted: true);
            model.Password = "abcdefgh";
            model.ConfirmPassword = "abcdefgh";

            var result = await controller.Register(model);

            // Redisplay с identity грешките (паролата), НО атомарно: НИКАКЪВ Clinic ред.
            var view = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);

            var clinicsAfter = await raw.Clinics.CountAsync();
            Assert.Equal(clinicsBefore, clinicsAfter);
            Assert.Equal(0, await raw.Users.CountAsync(u => u.Email == model.Email));
        }

        [Fact]
        public async Task Register_DuplicateEmail_FailsRollsBack_NoSecondUser()
        {
            var email = $"dup-{Guid.NewGuid():N}@test.bg";

            await using (var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: true))
            {
                var first = ValidModel(consentAccepted: true);
                first.Email = email;
                var firstResult = await harness.Controller.Register(first);
                Assert.IsType<RedirectToActionResult>(firstResult);
            }

            await using var raw = _fixture.CreateContext(new TestClinicContext(null));
            var clinicsBefore = await raw.Clinics.CountAsync();

            await using (var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: true))
            {
                var second = ValidModel(consentAccepted: true);
                second.Email = email;
                var result = await harness.Controller.Register(second);

                var view = Assert.IsType<ViewResult>(result);
                Assert.False(harness.Controller.ModelState.IsValid);
                Assert.Equal(clinicsBefore, await raw.Clinics.CountAsync());
            }
        }

        private static RegisterViewModel ValidModel(bool consentAccepted) => new()
        {
            ClinicName = $"Клиника {Guid.NewGuid():N}".Substring(0, 30),
            DisplayName = "Д-р Тестов",
            Email = $"reg-{Guid.NewGuid():N}@test.bg",
            Password = IdentityTestHarness.TestPassword,
            ConfirmPassword = IdentityTestHarness.TestPassword,
            ConsentAccepted = consentAccepted
        };
    }
}
