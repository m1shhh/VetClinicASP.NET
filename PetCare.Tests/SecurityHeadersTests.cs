using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PetCare.Tests.Infrastructure;
using Xunit;

namespace PetCare.Tests
{
    /// <summary>
    /// Task 12, част 1 (Review Focus #5 не го покрива — това е WAF тестът за headers):
    /// в Production всяка заявка носи nosniff / Referrer-Policy / X-Frame-Options / точния CSP
    /// от плана + HSTS при HTTPS; в Development НИКАКВИ security headers (HTTP-то работи).
    ///
    /// HSTS middleware слага Strict-Transport-Security САМО на HTTPS отговори — затова
    /// Production клиентът е с BaseAddress https://localhost (TestServer отразява scheme-а).
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class SecurityHeadersTests
    {
        private const string ExactCsp =
            "default-src 'self'; img-src 'self' https:; style-src 'self' 'unsafe-inline'; " +
            "script-src 'self' 'unsafe-inline'; font-src 'self'; connect-src 'self'; " +
            "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

        private readonly SqlTestDatabaseFixture _fixture;

        public SecurityHeadersTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Production_ResponseCarriesSecurityHeaders_AndExactCsp_AndHsts()
        {
            using var factory = new PetCareWebApplicationFactory(_fixture.ConnectionString, "Production");
            // ВАЖНО: не "https://localhost" — HSTS middleware-ът има вграден ExcludedHosts
            // списък (localhost/127.0.0.1/[::1]) и НЕ слага Strict-Transport-Security за тях.
            // Произволен HTTPS host минава през TestServer-а и получава HSTS.
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://petcare.test"),
                AllowAutoRedirect = false
            });

            var response = await client.GetAsync("/Account/Login");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Equal("nosniff", SingleHeader(response, "X-Content-Type-Options"));
            Assert.Equal("strict-origin-when-cross-origin", SingleHeader(response, "Referrer-Policy"));
            Assert.Equal("DENY", SingleHeader(response, "X-Frame-Options"));
            Assert.Equal(ExactCsp, SingleHeader(response, "Content-Security-Policy"));

            // HSTS: UseHsts() + HTTPS заявка → header присъства.
            Assert.True(response.Headers.Contains("Strict-Transport-Security"));
            Assert.Contains("max-age=", SingleHeader(response, "Strict-Transport-Security"));
        }

        [Fact]
        public async Task Development_ResponseHasNoSecurityHeaders()
        {
            using var factory = new PetCareWebApplicationFactory(_fixture.ConnectionString, "Development");
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false
            });

            var response = await client.GetAsync("/Account/Login");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.False(response.Headers.Contains("X-Content-Type-Options"));
            Assert.False(response.Headers.Contains("Referrer-Policy"));
            Assert.False(response.Headers.Contains("X-Frame-Options"));
            Assert.False(response.Headers.Contains("Content-Security-Policy"));
            Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        }

        private static string SingleHeader(HttpResponseMessage response, string name)
        {
            Assert.True(response.Headers.TryGetValues(name, out var values), $"Липсва header {name}");
            return Assert.Single(values!);
        }
    }
}
