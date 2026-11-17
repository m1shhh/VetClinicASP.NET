using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetClinic.Security;
using VetClinic.Services;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Персонал + линк Veterinarian (Task 8):
    /// - CreateStaff е АТОМАРЕН (user + Veterinarian ред + роля в ЕДНА транзакция) и приема
    ///   само трите клинични роли;
    /// - SetStaffActive(false) деактивира САМО акаунта — Veterinarian профилът и
    ///   историческите данни (appointments) остават непокътнати;
    /// - ListStaff е ИЗРИЧНО филтриран по ClinicId (ApplicationUser НЯМА query filter —
    ///   deny-by-default не е автоматичен за Identity таблиците).
    /// Harness-ът е моделът на IdentityTestHarness: ИСТИНСКИ UserManager над fixture
    /// контекст, като UserManager-ът и StaffService-ът делят ЕДНА context-инстанция.
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class StaffServiceTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public StaffServiceTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task CreateStaff_Vet_CreatesUserWithRoleAndLinkedVeterinarian()
        {
            var clinicId = SeedClinic();
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);

            var suffix = UniqueSuffix();
            var email = $"vet-{suffix}@clinic.bg";
            var displayName = $"Д-р Иван Ветев {suffix}";

            var result = harness.StaffService.CreateStaff(email, displayName, StaffTestHarness.TestPassword, AppRoles.Vet);

            Assert.True(result.Succeeded);

            var user = await harness.UserManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal(clinicId, user!.ClinicId);
            Assert.True(user.IsActive);
            Assert.True(await harness.UserManager.IsInRoleAsync(user, AppRoles.Vet));
            Assert.NotNull(user.VeterinarianId);

            // Свързаният Veterinarian ред (суров контекст — независимо от query filter-ите):
            // FullName == DisplayName и ClinicId == ТЕКУЩАТА клиника.
            await using var raw = CreateRawContext();
            var vet = await raw.Veterinarians.IgnoreQueryFilters()
                .SingleAsync(v => v.VetId == user.VeterinarianId);
            Assert.Equal(displayName, vet.FullName);
            Assert.Equal(clinicId, vet.ClinicId);
        }

        [Fact]
        public async Task CreateStaff_Receptionist_CreatesUserWithoutVeterinarianRow()
        {
            var clinicId = SeedClinic();
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);

            var suffix = UniqueSuffix();
            var email = $"reception-{suffix}@clinic.bg";
            var displayName = $"Мария Рецепционна {suffix}";

            var result = harness.StaffService.CreateStaff(email, displayName, StaffTestHarness.TestPassword, AppRoles.Receptionist);

            Assert.True(result.Succeeded);

            var user = await harness.UserManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal(clinicId, user!.ClinicId);
            Assert.True(await harness.UserManager.IsInRoleAsync(user, AppRoles.Receptionist));
            Assert.Null(user.VeterinarianId);

            // НИКАКЪВ Veterinarian ред за служител, който не е лекар.
            await using var raw = CreateRawContext();
            Assert.False(await raw.Veterinarians.IgnoreQueryFilters()
                .AnyAsync(v => v.FullName == displayName));
        }

        [Fact]
        public async Task CreateStaff_InvalidRole_FailsWithoutCreatingUserOrVeterinarian()
        {
            var clinicId = SeedClinic();
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);

            var suffix = UniqueSuffix();
            var email = $"super-{suffix}@clinic.bg";
            var displayName = $"Опит за оператор {suffix}";

            var result = harness.StaffService.CreateStaff(email, displayName, StaffTestHarness.TestPassword, "SuperAdmin");

            Assert.False(result.Succeeded);

            // НИКАКЪВ user ред и НИКАКЪВ Veterinarian ред — валидацията на ролята е ПЪРВА.
            await using var raw = CreateRawContext();
            Assert.Equal(0, await raw.Users.CountAsync(u => u.Email == email));
            Assert.False(await raw.Veterinarians.IgnoreQueryFilters()
                .AnyAsync(v => v.FullName == displayName));
        }

        [Fact]
        public async Task SetStaffActive_False_DeactivatesUserKeepsVeterinarianRowAndHistory()
        {
            var clinicId = SeedClinic();
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);

            var suffix = UniqueSuffix();
            var email = $"vet2-{suffix}@clinic.bg";
            var displayName = $"Д-р Елена Здравкова {suffix}";

            Assert.True(harness.StaffService
                .CreateStaff(email, displayName, StaffTestHarness.TestPassword, AppRoles.Vet)
                .Succeeded);

            var user = (await harness.UserManager.FindByEmailAsync(email))!;
            Assert.NotNull(user.VeterinarianId);
            var vetId = user.VeterinarianId!.Value;

            // Историческа данна за ТОЗИ vet (суров seed): appointment с patient/owner.
            await using var raw = CreateRawContext();
            var owner = new Owner { FullName = $"Стопанин {suffix}", ClinicId = clinicId };
            raw.Owners.Add(owner);
            raw.SaveChanges();

            var patient = new Patient
            {
                Name = $"Пациент {suffix}",
                Species = "Куче",
                OwnerId = owner.OwnerId,
                ClinicId = clinicId
            };
            raw.Patients.Add(patient);
            raw.SaveChanges();

            var appointment = new Appointment
            {
                PatientId = patient.PatientId,
                VetId = vetId,
                Reason = $"Преглед {suffix}",
                AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                AppointmentTime = new TimeOnly(10, 0),
                DurationMinutes = 30,
                ClinicId = clinicId
            };
            raw.Appointments.Add(appointment);
            raw.SaveChanges();

            // ДЕЙСТВИЕ: деактивиране на акаунта.
            harness.StaffService.SetStaffActive(user.Id, active: false);

            var deactivated = await harness.UserManager.FindByEmailAsync(email);
            Assert.NotNull(deactivated);
            Assert.False(deactivated!.IsActive);

            // Veterinarian профилът ОСТАВА (деактивирането НЕ трие профила).
            var vetAfter = await raw.Veterinarians.IgnoreQueryFilters()
                .SingleAsync(v => v.VetId == vetId);
            Assert.Equal(displayName, vetAfter.FullName);

            // Историческата данна е непокътната.
            var appointmentAfter = await raw.Appointments.IgnoreQueryFilters()
                .SingleAsync(a => a.AppointmentId == appointment.AppointmentId);
            Assert.Equal(vetId, appointmentAfter.VetId);
        }

        [Fact]
        public async Task ListStaff_ReturnsOnlyCurrentClinicStaff()
        {
            var clinicId = SeedClinic();
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, clinicId);

            var suffix = UniqueSuffix();
            var email = $"admin5-{suffix}@clinic.bg";

            Assert.True(harness.StaffService
                .CreateStaff(email, $"Админ Петя {suffix}", StaffTestHarness.TestPassword, AppRoles.ClinicAdmin)
                .Succeeded);

            var staff = harness.StaffService.ListStaff();

            Assert.Contains(staff, u => u.Email == email);
        }

        [Fact]
        public async Task ListStaff_OtherClinicContext_ReturnsEmpty()
        {
            var clinicAId = SeedClinic();
            var clinicBId = SeedClinic();

            await using var harnessA = await StaffTestHarness.BuildAsync(_fixture, clinicAId);
            var suffix = UniqueSuffix();
            var email = $"admin6-{suffix}@clinic.bg";

            Assert.True(harnessA.StaffService
                .CreateStaff(email, $"Админ А {suffix}", StaffTestHarness.TestPassword, AppRoles.ClinicAdmin)
                .Succeeded);

            // Контекст на клиника Б: чужд персонал НЕ изтича (изричният ClinicId филтър).
            await using var harnessB = await StaffTestHarness.BuildAsync(_fixture, clinicBId);

            Assert.Empty(harnessB.StaffService.ListStaff());
        }

        [Fact]
        public async Task ListStaff_WithoutClinicContext_ReturnsEmpty()
        {
            // Deny-by-default за ListStaff: без клиничен контекст → празен списък, не throw.
            await using var harness = await StaffTestHarness.BuildAsync(_fixture, null);

            Assert.Empty(harness.StaffService.ListStaff());
        }

        [Fact]
        public async Task SetStaffActive_ForeignClinicUserId_IsSilentlyIgnored()
        {
            var clinicAId = SeedClinic();
            var clinicBId = SeedClinic();

            await using var harnessA = await StaffTestHarness.BuildAsync(_fixture, clinicAId);
            var suffix = UniqueSuffix();
            var email = $"admin7-{suffix}@clinic.bg";

            Assert.True(harnessA.StaffService
                .CreateStaff(email, $"Админ А {suffix}", StaffTestHarness.TestPassword, AppRoles.ClinicAdmin)
                .Succeeded);
            var userA = (await harnessA.UserManager.FindByEmailAsync(email))!;

            // Контекст на клиника Б + userId на потребител от клиника А → "не е намерен"
            // → тихо не-действие (без throw, без промяна).
            await using var harnessB = await StaffTestHarness.BuildAsync(_fixture, clinicBId);
            harnessB.StaffService.SetStaffActive(userA.Id, active: false);

            var after = await harnessA.UserManager.FindByEmailAsync(email);
            Assert.NotNull(after);
            Assert.True(after!.IsActive);
        }

        // =========================================================
        // Помощни методи
        // =========================================================

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

        private VetClinicContext CreateRawContext() =>
            // Суров контекст без клиничен контекст: за seed и за проверки "какво е В БАЗАТА"
            // (IgnoreQueryFilters — query filter-ите при null-clinic иначе връщат нула реда).
            _fixture.CreateContext(new TestClinicContext(null));

        private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// Harness за StaffService (моделът на IdentityTestHarness): ИСТИНСКИ UserManager/RoleManager
    /// над fixture-базиран VetClinicContext. КЛЮЧОВО: UserManager-ът, RoleManager-ът и
    /// StaffService-ът делят ЕДНА scoped VetClinicContext-инстанция (от root provider) —
    /// затова EF транзакцията в CreateStaff покрива user + Veterinarian + роля едновременно
    /// (същият механизъм като AccountController.Register).
    /// Системните роли (справочници, не tenant данни) се seed-ват idempotentно — Task 9
    /// ще ги seed-ва в production startup.
    /// </summary>
    internal static class StaffTestHarness
    {
        public const string TestPassword = "Password1";

        public static async Task<Harness> BuildAsync(
            SqlTestDatabaseFixture fixture, int? clinicId, int? vetProfileId = null)
        {
            var currentClinic = new TestClinicContext(clinicId, vetProfileId);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentClinic>(currentClinic);
            services.AddSingleton(fixture.Encryption);
            services.AddSingleton(new DbContextOptionsBuilder<VetClinicContext>()
                .UseSqlServer(fixture.ConnectionString)
                .Options);
            services.AddScoped<VetClinicContext>(sp => new VetClinicContext(
                sp.GetRequiredService<DbContextOptions<VetClinicContext>>(),
                sp.GetRequiredService<ICurrentClinic>(),
                sp.GetRequiredService<EncryptionService>()));

            // ТОЧНО същите Identity options като Program.cs (Global Constraints).
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
            .AddEntityFrameworkStores<VetClinicContext>();

            services.AddScoped<IStaffService, StaffService>();

            var provider = services.BuildServiceProvider();

            var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var roleName in new[] { AppRoles.ClinicAdmin, AppRoles.Vet, AppRoles.Receptionist })
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // ЕДНА context-инстанция за UserManager (през EF store-а) и StaffService —
            // задължително за атомарността на транзакцията в CreateStaff.
            var context = provider.GetRequiredService<VetClinicContext>();
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var staffService = provider.GetRequiredService<IStaffService>();

            return new Harness(context, userManager, staffService, provider);
        }

        public sealed record Harness(
            VetClinicContext Context,
            UserManager<ApplicationUser> UserManager,
            IStaffService StaffService,
            ServiceProvider Provider) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => Provider.DisposeAsync();
        }
    }
}
