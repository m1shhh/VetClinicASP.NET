using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PetCare.Tests.Infrastructure;
using VetClinicASP.NET.Infrastructure;
using Xunit;

namespace PetCare.Tests
{
    /// <summary>
    /// Task 13: privacy страница + consent текст v1.
    /// - TODO-guard: включена публична регистрация без Contact:Email → WARNING (без crash);
    ///   с имейл или при изключена регистрация → никакво предупреждение (default поведение);
    /// - /Home/Privacy рендерира реалния текст през ЦЕЛИЯ Program.cs pipeline (WAF) —
    ///   оператор, регион, криптиране и контактната формулировка присъстват.
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class PrivacyConfigurationGuardTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public PrivacyConfigurationGuardTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void PublicRegistrationEnabled_WithoutContactEmail_LogsWarning_AndDoesNotThrow()
        {
            var configuration = BuildConfiguration(
                publicRegistration: true,
                contactEmail: null);
            var captured = new CapturingLogger();

            var warned = PrivacyConfigurationGuard.Check(configuration, captured);

            Assert.True(warned);
            Assert.Contains(captured.Entries, e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains("Contact:Email липсва"));
        }

        [Fact]
        public void PublicRegistrationEnabled_WithContactEmail_LogsNoWarning()
        {
            var configuration = BuildConfiguration(
                publicRegistration: true,
                contactEmail: "privacy@petcare.bg");
            var captured = new CapturingLogger();

            var warned = PrivacyConfigurationGuard.Check(configuration, captured);

            Assert.False(warned);
            Assert.DoesNotContain(captured.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public void PublicRegistrationDisabled_WithoutContactEmail_LogsNoWarning()
        {
            var configuration = BuildConfiguration(
                publicRegistration: false,
                contactEmail: null);
            var captured = new CapturingLogger();

            var warned = PrivacyConfigurationGuard.Check(configuration, captured);

            Assert.False(warned);
            Assert.DoesNotContain(captured.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task PrivacyPage_RendersRealNotice_WithKeyPhrases()
        {
            // Development: без HTTPS/HSTS особености на Production pipeline-а; страницата е
            // анонимна и не пипа базата (HomeController.Privacy само връща View).
            using var factory = new PetCareWebApplicationFactory(
                _fixture.ConnectionString, "Development");
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false
            });

            var response = await client.GetAsync("/Home/Privacy");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("Мишо", html);
            Assert.Contains("физическо лице", html);
            Assert.Contains("West Europe", html);
            Assert.Contains("AES-256-GCM", html);
            Assert.Contains("пълно изтриване", html);
            Assert.Contains("Контакт: предстои", html);
            Assert.Contains("v1", html);
        }

        [Fact]
        public async Task RegisterPage_WithPublicRegistration_LinksToFullConsentTextV1()
        {
            // Съгласието, което контролерът записва като ConsentTextVersion="v1", трябва да е
            // ДОСТЪПНО от формата — отметката сочи към пълния текст на privacy страницата.
            using var factory = new PetCareWebApplicationFactory(
                _fixture.ConnectionString,
                "Development",
                new Dictionary<string, string?>
                {
                    ["Features:PublicRegistration"] = "true"
                });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false
            });

            var response = await client.GetAsync("/Account/Register");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("/Home/Privacy", html);
            Assert.Contains("v1", html);
        }

        private static IConfiguration BuildConfiguration(
            bool publicRegistration, string? contactEmail)
        {
            var values = new Dictionary<string, string?>
            {
                ["Features:PublicRegistration"] = publicRegistration ? "true" : "false"
            };

            if (contactEmail is not null)
            {
                values["Contact:Email"] = contactEmail;
            }

            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }

        /// <summary>Минимален capture-ващ logger (същият модел като OperatorPanelTests).</summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
                NullLoggerScope.Instance;

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
}
