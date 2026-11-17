using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetClinic.Models.ViewModels;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Lockout (Task 7, Global Constraints): 5 грешни пароли → 15 мин блокиран акаунт;
    /// 6-ти опит с ВЯРНА парола → пак отказан (LockedOut). Плюс: деактивиран/липсващ
    /// акаунт → ЕДНАКЪВ общ меседж (user enumeration защита — никога не разкриваме кой).
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class LockoutTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public LockoutTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Login_FiveWrongPasswords_LocksAccount_SixthCorrectPasswordRefused()
        {
            var email = $"lock-{Guid.NewGuid():N}@test.bg";
            await SeedClinicAndUserAsync(email, IdentityTestHarness.TestPassword);

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);

            var userManager = harness.Provider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            // 5 грешни POST Login опита (lockoutOnFailure: true).
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                var controller = harness.NewController();
                var result = await controller.Login(new LoginViewModel
                {
                    Email = email,
                    Password = "WrongPass1"
                });

                if (attempt < 5)
                {
                    // Още без lockout — обща грешка, обратно на формата.
                    var view = Assert.IsType<ViewResult>(result);
                    Assert.False(controller.ModelState.IsValid);
                }
                else
                {
                    // 5-тият опит СЕ собствено заключва акаунта → LockedOut.
                    var redirect = Assert.IsType<RedirectToActionResult>(result);
                    Assert.Equal(nameof(VetClinic.Controllers.AccountController.Locked), redirect.ActionName);
                }
            }

            // UserManager.IsLockedOutAsync → true след 5-те опита.
            Assert.True(await userManager.IsLockedOutAsync(user!));

            // 6-ти опит с ВЯРНА парола → пак отказан (refused, не успешен вход).
            var sixth = harness.NewController();
            var sixthResult = await sixth.Login(new LoginViewModel
            {
                Email = email,
                Password = IdentityTestHarness.TestPassword
            });

            var refused = Assert.IsType<RedirectToActionResult>(sixthResult);
            Assert.Equal(nameof(VetClinic.Controllers.AccountController.Locked), refused.ActionName);
            Assert.True(await userManager.IsLockedOutAsync(user!));
        }

        [Fact]
        public async Task Login_DeactivatedAccount_GenericErrorWithoutRevealingCause()
        {
            var email = $"off-{Guid.NewGuid():N}@test.bg";
            await SeedClinicAndUserAsync(email, IdentityTestHarness.TestPassword);

            // Деактивиране чрез суров EF update (IsActive=false — основание за отказ на вход).
            await using (var raw = _fixture.CreateContext(new TestClinicContext(null)))
            {
                var dbUser = await raw.Users.SingleAsync(u => u.Email == email);
                dbUser.IsActive = false;
                await raw.SaveChangesAsync();
            }

            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);
            var controller = harness.Controller;

            var result = await controller.Login(new LoginViewModel
            {
                Email = email,
                Password = IdentityTestHarness.TestPassword // ВЯРНА парола — пак отказан
            });

            var view = Assert.IsType<ViewResult>(result);
            var error = Assert.Single(controller.ModelState[string.Empty]!.Errors);
            // Общ меседж — същият като при несъществуващ имейл, без разкриване на причината.
            Assert.Equal("Невалидни данни за вход.", error.ErrorMessage);
        }

        [Fact]
        public async Task Login_UnknownEmail_SameGenericErrorAsDeactivated()
        {
            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);
            var controller = harness.Controller;

            var result = await controller.Login(new LoginViewModel
            {
                Email = $"nobody-{Guid.NewGuid():N}@test.bg",
                Password = "Whatever1"
            });

            var view = Assert.IsType<ViewResult>(result);
            var error = Assert.Single(controller.ModelState[string.Empty]!.Errors);
            Assert.Equal("Невалидни данни за вход.", error.ErrorMessage);
        }

        /// <summary>Clinic ред (активен) + ApplicationUser чрез UserManager.CreateAsync директно.</summary>
        private async Task SeedClinicAndUserAsync(string email, string password)
        {
            int clinicId;
            await using (var context = _fixture.CreateContext(new TestClinicContext(null)))
            {
                var clinic = new Clinic
                {
                    Name = $"Лок-клиника {Guid.NewGuid():N}".Substring(0, 30),
                    CreatedAtUtc = DateTime.UtcNow,
                    IsActive = true
                };
                context.Clinics.Add(clinic);
                await context.SaveChangesAsync();
                clinicId = clinic.ClinicId;
            }

            await using (var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false))
            {
                var userManager = harness.Provider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    DisplayName = "Д-р Локов",
                    ClinicId = clinicId,
                    IsActive = true
                };
                var createResult = await userManager.CreateAsync(user, password);
                Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }
        }
    }
}
