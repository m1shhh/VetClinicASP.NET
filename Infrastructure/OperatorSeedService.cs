using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinicASP.NET.Infrastructure
{
    /// <summary>
    /// Bootstrap seed при стартиране (Task 9, design doc §6):
    /// 1. Ролите се сидват idempotentно ПЪРВО — Operator/ClinicAdmin/Vet/Receptionist
    ///    (бележка (а) от Task 7 ревю: guard при всяко стартиране; публичният статичен
    ///    <see cref="EnsureRolesAsync"/> е тестово-приятелският entry point).
    /// 2. Ако НИТОН един user няма роля Operator И конфигурацията дефинира
    ///    Operator:Email + Operator:InitialPassword → създава Operator акаунт
    ///    (ClinicId = null, DisplayName "Оператор", IsActive, без clinic_id claim).
    /// 3. Ключовете липсват → WARNING в лога и ИЗХОД БЕЗ crash (същото като dev, където
    ///    ключовете стоят unset в user-secrets — собственикът решава кога да ги включи).
    /// 4. Operator вече съществува → no-op.
    ///
    /// HostedService е singleton → всичко се тегли през СВЕЖЕ scopes (IServiceScopeFactory).
    /// В режим `--encrypt-existing` seed-ът НЕ стартира: runner пътят в Program.cs прави
    /// builder.Build() и връща ПРЕДИ app.Run() — IHostedService.StartAsync се викат само при
    /// стартиране на хоста (app.Run), затова редът на регистрацията (ПРЕДИ Build) е достатъчен.
    /// Никаква default парола в кода — ключовете идват само от конфигурация (env/user-secrets).
    /// </summary>
    public class OperatorSeedService : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OperatorSeedService> _logger;

        public OperatorSeedService(
            IConfiguration configuration,
            IServiceScopeFactory scopeFactory,
            ILogger<OperatorSeedService> logger)
        {
            _configuration = configuration;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await EnsureRolesAsync(roleManager);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var existingOperators = await userManager.GetUsersInRoleAsync(AppRoles.Operator);
            if (existingOperators.Count > 0)
            {
                _logger.LogInformation("Operator seed: Operator акаунт вече съществува — no-op.");
                return;
            }

            var email = _configuration["Operator:Email"];
            var initialPassword = _configuration["Operator:InitialPassword"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(initialPassword))
            {
                // Задължителен warning + тих изход — seed-ът НЕ блокира стартирането.
                _logger.LogWarning(
                    "Operator seed пропуснат — конфигурирайте Operator:Email и Operator:InitialPassword");
                return;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = "Оператор",
                ClinicId = null, // design doc §3.3: операторът е БЕЗ ClinicId (deny-by-default)
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(user, initialPassword);
            if (!createResult.Succeeded)
            {
                // Провал при seed = конфигурационен проблем, не crash — операторът може да се
                // създаде повторно при следващо стартиране след корекция на ключовете.
                _logger.LogError(
                    "Operator seed неуспешен за {Email}: {Errors}",
                    email,
                    string.Join("; ", createResult.Errors.Select(e => $"{e.Code}: {e.Description}")));
                return;
            }

            // Резултатът от AddToRoleAsync НЕ се пренебрегва (урок от Task 7 ревю).
            var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Operator);
            if (!roleResult.Succeeded)
            {
                _logger.LogError(
                    "Operator seed: неуспешно добавяне на роля {Role} за {Email}: {Errors}",
                    AppRoles.Operator,
                    email,
                    string.Join("; ", roleResult.Errors.Select(e => $"{e.Code}: {e.Description}")));
                return;
            }

            _logger.LogInformation("Operator seed: създаден Operator акаунт за {Email}.", email);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Idempotent role seed (бележка (а) от Task 7 ревю): четирите системни роли са
        /// справочници (не tenant данни) — създават се САМО ако липсват. Публично статичен,
        /// за да се извиква и от тестовете (OperatorTestHarness).
        /// </summary>
        public static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
        {
            foreach (var roleName in new[] { AppRoles.Operator, AppRoles.ClinicAdmin, AppRoles.Vet, AppRoles.Receptionist })
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(roleName));
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(
                            $"Role seed неуспешен за {roleName}: " +
                            string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
                    }
                }
            }
        }
    }
}
