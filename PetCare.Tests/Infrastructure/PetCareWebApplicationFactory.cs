using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PetCare.Tests.Infrastructure
{
    /// <summary>
    /// WAF върху ЦЕЛИЯ Program.cs pipeline за Task 12 тестовете (headers, rate limiter,
    /// ForwardedHeaders).
    ///
    /// Конфигурацията се подава ПРЕДИ app-ът да се build-не чрез IWebHostBuilder.UseSetting
    /// (host конфигурацията е част от WebApplicationBuilder.Configuration в minimal hosting):
    ///  - ConnectionStrings:DefaultConnection → ephemeral базата на SqlTestDatabaseFixture
    ///    (НИКОГА dev VetClinic базата);
    ///  - Encryption:MasterKey → случаен 32-байтов ключ за процеса (никога реален secret);
    ///  - Operator:* остават unset → seed-ът логва warning и не създава акаунт;
    ///  - ReverseProxy:Enabled → само при изрично подаден setting (по подразбиране липсва).
    ///
    /// Environment се задава per фабрика ("Production" за headers, "Development" за
    /// негативния тест).
    /// </summary>
    internal sealed class PetCareWebApplicationFactory : WebApplicationFactory<Program>
    {
        // Един ключ за цялото изпълнение: EF кешира ЕДИН модел за тип context — смесени
        // ключове между фабрики биха „отровили" конверторите. WAF тестовете не пишат
        // криптирани колони, така че дори fixture-овият модел да е кеширан пръв, е безопасно.
        private static readonly string ThrowawayMasterKey =
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        private readonly string _connectionString;
        private readonly string _environment;
        private readonly IReadOnlyDictionary<string, string?> _settings;

        public PetCareWebApplicationFactory(
            string connectionString,
            string environment,
            IReadOnlyDictionary<string, string?>? settings = null)
        {
            _connectionString = connectionString;
            _environment = environment;
            _settings = settings ?? new Dictionary<string, string?>();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);

            builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
            builder.UseSetting("Encryption:MasterKey", ThrowawayMasterKey);

            foreach (var setting in _settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        }
    }
}
