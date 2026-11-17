using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VetClinic.Security;
using VetClinic.Services;
using VetClinicASP.NET.Infrastructure;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Операторски панел (Task 9, design doc §3.3/§8):
    /// - GetClinics връща МЕТАДАННИ САМО за всички клиники (IgnoreQueryFilters) — агрегатните
    ///   брояци доказват, че tenant филтрите са вдигнати (иначе deny-by-default би върнал 0);
    ///   формата на OperatorClinicRow Е compile-level PII assertion-ът — няма поле за имена
    ///   на пациенти/стопани/лекари или друго tenant съдържание;
    /// - SetActive превключва Clinic.IsActive директно в БД (Clinic не е query-филтриран);
    /// - OperatorSeedService: без Operator:* ключове → WARNING + никакъв user (без crash);
    ///   с ключове → Operator user (ClinicId = null, DisplayName "Оператор"); повторно
    ///   пускане → no-op; role-сидването е публичен статичен, idempotentен метод
    ///   (бележка (а) от Task 7 ревю);
    /// - clinic_id claim assertion (бележка (в) от Task 7 ревю): principal-ът от claims
    ///   factory-то носи clinic_id == ClinicId; операторският user НЕ носи такъв claim.
    /// Harness-ът е моделът на StaffTestHarness: ИСТИНСКИ UserManager/RoleManager над fixture
    /// контекст, като UserManager-ът и metadata service-ът делят ЕДНА context-инстанция
    /// (атомарната транзакция в CreatePilotClinic). ICurrentClinic = TestClinicContext(null)
    /// — операторският потребител няма клиничен контекст.
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class OperatorPanelTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public OperatorPanelTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        // =========================================================
        // GetClinics — метаданни САМО (design doc §3.3)
        // =========================================================

        [Fact]
        public async Task GetClinics_ReturnsRowsForBothClinicsWithCorrectAggregateCounts()
        {
            await using var harness = await OperatorTestHarness.BuildAsync(_fixture);
            var suffix = UniqueSuffix();

            var clinicA = harness.Metadata.CreatePilotClinic(
                $"Клиника А {suffix}", $"admin-a-{suffix}@clinic.bg", $"Админ А {suffix}", OperatorTestHarness.TestPassword);
            var clinicB = harness.Metadata.CreatePilotClinic(
                $"Клиника Б {suffix}", $"admin-b-{suffix}@clinic.bg", $"Админ Б {suffix}", OperatorTestHarness.TestPassword);

            SeedTenantData(clinicA, patients: 2, appointments: 3);
            SeedTenantData(clinicB, patients: 1, appointments: 0);

            var rows = harness.Metadata.GetClinics();

            var rowA = rows.Single(r => r.ClinicId == clinicA);
            var rowB = rows.Single(r => r.ClinicId == clinicB);

            // Агрегатите за ДВЕ клиники — операторският контекст (null-clinic) е достатъчен:
            // IgnoreQueryFilters-ът е активен, иначе query filter-ите биха върнали 0/0.
            Assert.Equal(2, rowA.Patients);
            Assert.Equal(3, rowA.Appointments);
            Assert.True(rowA.IsActive);
            Assert.Equal(nameof(ClinicBillingStatus.Active), rowA.BillingStatus);

            Assert.Equal(1, rowB.Patients);
            Assert.Equal(0, rowB.Appointments);
        }

        [Fact]
        public void OperatorClinicRow_HasNoTenantContentProperties()
        {
            // Compile-level PII assertion (design doc §2 анти-цел "patient данни в
            // операторския панел"): формата на записа Е доказателството — точно 7 полета,
            // НИКАКЪВ patient/owner/vet идентификатор, причина, диагноза или URL за детайлен преглед.
            var properties = typeof(OperatorClinicRow)
                .GetProperties()
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[] { "Appointments", "BillingStatus", "ClinicId", "CreatedAtUtc", "IsActive", "Name", "Patients" },
                properties);
        }

        // =========================================================
        // SetActive — незабавно превключване на Clinic.IsActive
        // =========================================================

        [Fact]
        public async Task SetActive_False_DeactivatesClinicInDatabase_TrueRestores()
        {
            await using var harness = await OperatorTestHarness.BuildAsync(_fixture);
            var suffix = UniqueSuffix();
            var clinicId = harness.Metadata.CreatePilotClinic(
                $"Клиника Toggle {suffix}", $"toggle-{suffix}@clinic.bg", $"Админ Toggle {suffix}", OperatorTestHarness.TestPassword);

            harness.Metadata.SetActive(clinicId, active: false);

            // Сурово четене на БАЗАТА (не наtracked обекта) — Clinic не е query-филтриран.
            await using var raw = CreateRawContext();
            Assert.False(await raw.Clinics.IgnoreQueryFilters()
                .Where(c => c.ClinicId == clinicId)
                .Select(c => c.IsActive)
                .SingleAsync());

            harness.Metadata.SetActive(clinicId, active: true);

            Assert.True(await raw.Clinics.IgnoreQueryFilters()
                .Where(c => c.ClinicId == clinicId)
                .Select(c => c.IsActive)
                .SingleAsync());
        }

        [Fact]
        public async Task SetActive_UnknownClinicId_ThrowsInvalidOperationException()
        {
            await using var harness = await OperatorTestHarness.BuildAsync(_fixture);

            var exception = Assert.Throws<InvalidOperationException>(
                () => harness.Metadata.SetActive(clinicId: int.MaxValue, active: false));

            Assert.Contains("Клиниката не е намерена", exception.Message);
        }

        // =========================================================
        // OperatorSeedService — bootstrap на ролите + Operator (design doc §6)
        // =========================================================

        [Fact]
        public async Task OperatorSeed_WithoutConfiguredKeys_LogsWarningAndCreatesNothing()
        {
            await using var harness = await OperatorTestHarness.BuildAsync(_fixture);
            await RemoveAllOperatorsAsync(harness.UserManager);

            // Конфигурация БЕЗ Operator:Email / Operator:InitialPassword — същото състояние като
            // dev (ключовете са UNSET в user-secrets; собственикът решава кога да ги включи).
            var configuration = new ConfigurationBuilder().Build();
            var captured = new CapturingLogger();
            var seed = new OperatorSeedService(
                configuration,
                harness.Provider.GetRequiredService<IServiceScopeFactory>(),
                captured);

            // НЕ crash-ва — warning пътят е изрично изискване.
            await seed.StartAsync(CancellationToken.None);

            Assert.Contains(captured.Entries, e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains("Operator seed пропуснат"));

            // Никакъв Operator user е създаден.
            Assert.Empty(await harness.UserManager.GetUsersInRoleAsync(AppRoles.Operator));

            // Но ролите са сиднати idempotentно ПРЕДИ изхода (бележка (а) от Task 7 ревю).
            var roleManager = harness.Provider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { AppRoles.Operator, AppRoles.ClinicAdmin, AppRoles.Vet, AppRoles.Receptionist })
            {
                Assert.True(await roleManager.RoleExistsAsync(role), $"Ролята {role} липсва след seed-а.");
            }
        }

        [Fact]
        public async Task OperatorSeed_WithConfiguredKeys_CreatesOperatorUserWithoutClinic()
        {
            await using var harness = await OperatorTestHarness.BuildAsync(_fixture);
            await RemoveAllOperatorsAsync(harness.UserManager);

            var suffix = UniqueSuffix();
            var email = $"operator-seed-{suffix}@petcare.bg";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Operator:Email"] = email,
                    ["Operator:InitialPassword"] = "Operator1"
                })
                .Build();

            var seed = new OperatorSeedService(
                configuration,
                harness.Provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<OperatorSeedService>.Instance);

            await seed.StartAsync(CancellationToken.None);

            var operatorUser = await harness.UserManager.FindByEmailAsync(email);
            Assert.NotNull(operatorUser);
            Assert.Equal("Оператор", operatorUser!.DisplayName);
            Assert.Null(operatorUser.ClinicId); // design doc §3.3: операторът е БЕЗ ClinicId
            Assert.True(operatorUser.IsActive);
            Assert.True(await harness.UserManager.IsInRoleAsync(operatorUser, AppRoles.Operator));

            // Почистване — съществуващ Operator би направил следващите seed тестове no-op.
            await RemoveAllOperatorsAsync(harness.UserManager);
        }

        [Fact]
        public async Task OperatorSeed_WhenOperatorAlreadyExists_IsNoOp()
        {
            await using var harness = await OperatorTestHarness.BuildAsync(_fixture);
            await RemoveAllOperatorsAsync(harness.UserManager);

            static IConfiguration ConfigFor(string email) => new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Operator:Email"] = email,
                    ["Operator:InitialPassword"] = "Operator1"
                })
                .Build();

            var suffix = UniqueSuffix();
            var scopeFactory = harness.Provider.GetRequiredService<IServiceScopeFactory>();

            await new OperatorSeedService(ConfigFor($"operator-first-{suffix}@petcare.bg"), scopeFactory, NullLogger<OperatorSeedService>.Instance)
                .StartAsync(CancellationToken.None);

            var secondEmail = $"operator-second-{suffix}@petcare.bg";
            await new OperatorSeedService(ConfigFor(secondEmail), scopeFactory, NullLogger<OperatorSeedService>.Instance)
                .StartAsync(CancellationToken.None);

            // Само ПЪРВИЯт user е създаден; второто пускане е no-op.
            Assert.Single(await harness.UserManager.GetUsersInRoleAsync(AppRoles.Operator));
            Assert.Null(await harness.UserManager.FindByEmailAsync(secondEmail));

            await RemoveAllOperatorsAsync(harness.UserManager);
        }

        // =========================================================
        // clinic_id claim assertion (бележка (в) от Task 7 ревю)
        // =========================================================

        [Fact]
        public async Task ClaimsPrincipal_ContainsClinicIdClaim_ForClinicUser()
        {
            var clinicId = SeedClinic();
            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);
            var userManager = harness.Provider.GetRequiredService<UserManager<ApplicationUser>>();

            var suffix = UniqueSuffix();
            var email = $"claim-{suffix}@clinic.bg";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = $"Клиничен потребител {suffix}",
                ClinicId = clinicId,
                IsActive = true
            };
            Assert.True((await userManager.CreateAsync(user, IdentityTestHarness.TestPassword)).Succeeded);

            var factory = harness.Provider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
            var principal = await factory.CreateAsync(user);

            var clinicClaim = principal.FindFirst("clinic_id");
            Assert.NotNull(clinicClaim);
            Assert.Equal(clinicId.ToString(), clinicClaim!.Value);
        }

        [Fact]
        public async Task ClaimsPrincipal_OperatorUser_HasNoClinicIdClaim()
        {
            await using var harness = await IdentityTestHarness.BuildAsync(_fixture, publicRegistration: false);
            var userManager = harness.Provider.GetRequiredService<UserManager<ApplicationUser>>();

            var suffix = UniqueSuffix();
            var email = $"op-claim-{suffix}@petcare.bg";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = "Оператор без клиника",
                ClinicId = null,
                IsActive = true
            };
            Assert.True((await userManager.CreateAsync(user, IdentityTestHarness.TestPassword)).Succeeded);

            var factory = harness.Provider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
            var principal = await factory.CreateAsync(user);

            Assert.Null(principal.FindFirst("clinic_id"));
        }

        // =========================================================
        // Помощни методи
        // =========================================================

        /// <summary>Суров seed на tenant данни (пациенти + appointments) за дадена клиника.</summary>
        private void SeedTenantData(int clinicId, int patients, int appointments)
        {
            using var raw = CreateRawContext();
            var suffix = UniqueSuffix();

            var owner = new Owner { FullName = $"Стопанин {suffix}", ClinicId = clinicId };
            raw.Owners.Add(owner);

            var vet = new Veterinarian { FullName = $"Д-р Вет {suffix}", ClinicId = clinicId };
            raw.Veterinarians.Add(vet);
            raw.SaveChanges();

            var createdPatients = new List<Patient>();
            for (var i = 0; i < patients; i++)
            {
                var patient = new Patient
                {
                    Name = $"Пациент{i}{suffix}",
                    Species = "Куче",
                    OwnerId = owner.OwnerId,
                    ClinicId = clinicId
                };
                raw.Patients.Add(patient);
                createdPatients.Add(patient);
            }
            raw.SaveChanges();

            var basePatientId = createdPatients[0].PatientId;
            for (var i = 0; i < appointments; i++)
            {
                raw.Appointments.Add(new Appointment
                {
                    PatientId = basePatientId,
                    VetId = vet.VetId,
                    AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    AppointmentTime = new TimeOnly(9 + (i % 8), 0),
                    Reason = $"Преглед {i} {suffix}",
                    ClinicId = clinicId
                });
            }
            raw.SaveChanges();
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

        private static async Task RemoveAllOperatorsAsync(UserManager<ApplicationUser> userManager)
        {
            foreach (var user in await userManager.GetUsersInRoleAsync(AppRoles.Operator))
            {
                await userManager.DeleteAsync(user);
            }
        }

        private VetClinicContext CreateRawContext() =>
            // Суров контекст (null-clinic): за seed и за проверки "какво Е в БАЗАТА".
            _fixture.CreateContext(new TestClinicContext(null));

        private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];

        /// <summary>Минимален capture-ващ logger — за assert на warning-а от seed-а.</summary>
        private sealed class CapturingLogger : ILogger<OperatorSeedService>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLoggerScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }

            private sealed class NullLoggerScope : IDisposable
            {
                public static readonly NullLoggerScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }

    /// <summary>
    /// Harness за OperatorMetadataService + OperatorSeedService (моделът на StaffTestHarness):
    /// ИСТИНСКИ UserManager/RoleManager над fixture-базиран VetClinicContext, като UserManager-ът
    /// и metadata service-ът делят ЕДНА scoped context-инстанция — задължително за атомарната
    /// транзакция в CreatePilotClinic (същият механизъм като StaffService.CreateStaff).
    /// Ролите се сидват чрез ПУБЛИЧНИЯ статичен метод на OperatorSeedService — production пътят.
    /// </summary>
    internal static class OperatorTestHarness
    {
        public const string TestPassword = "Password1";

        public static async Task<Harness> BuildAsync(SqlTestDatabaseFixture fixture)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentClinic>(new TestClinicContext(null));
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

            services.AddScoped<IOperatorMetadataService, OperatorMetadataService>();

            var provider = services.BuildServiceProvider();

            // Ролите: СЪЩИЯТ idempotent seed като в production (Program.cs → OperatorSeedService).
            var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
            await OperatorSeedService.EnsureRolesAsync(roleManager);

            var context = provider.GetRequiredService<VetClinicContext>();
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var metadata = provider.GetRequiredService<IOperatorMetadataService>();

            return new Harness(context, userManager, metadata, provider);
        }

        public sealed record Harness(
            VetClinicContext Context,
            UserManager<ApplicationUser> UserManager,
            IOperatorMetadataService Metadata,
            ServiceProvider Provider) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => Provider.DisposeAsync();
        }
    }
}
