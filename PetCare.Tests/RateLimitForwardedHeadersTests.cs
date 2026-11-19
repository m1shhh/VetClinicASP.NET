using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PetCare.Tests.Infrastructure;
using Xunit;

namespace PetCare.Tests
{
    /// <summary>
    /// Task 12, част 2–3 (Review Focus #5): логин limiter 10/мин на IP → 429 на 11-ата
    /// заявка; зад proxy (`ReverseProxy:Enabled=true`) X-Forwarded-For разделя bucket-ите,
    /// без него всички заявки падат в общия bucket на proxy-то/TestServer-а.
    ///
    /// Забележка: POST-овете НЯМАТ antiforgery токен — MVC отговаря 400. Това е ОК:
    /// limiter-ът е middleware ПРЕДИ MVC, затова първите 10 са НЕ-429 (400), а 11-ата е 429.
    /// Така тестът мери точно limiter-а, без да зависи от login логиката.
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class RateLimitForwardedHeadersTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public RateLimitForwardedHeadersTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Login_EleventhPostFromSameClient_Returns429()
        {
            using var factory = new PetCareWebApplicationFactory(_fixture.ConnectionString, "Development");
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false
            });

            for (var i = 0; i < 10; i++)
            {
                var response = await PostLoginAsync(client);
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            }

            var eleventh = await PostLoginAsync(client);
            Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        }

        [Fact]
        public async Task ReverseProxyEnabled_SeparatesBucketsByForwardedFor()
        {
            using var factory = CreateForwardedHeadersFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false
            });

            // 1.2.3.4 изчерпва своите 10 за минута...
            for (var i = 0; i < 10; i++)
            {
                var response = await PostLoginAsync(client, "1.2.3.4");
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            }

            var eleventhSameIp = await PostLoginAsync(client, "1.2.3.4");
            Assert.Equal(HttpStatusCode.TooManyRequests, eleventhSameIp.StatusCode);

            // ...но 5.6.7.8 е СВЕЖ bucket (без ForwardedHeaders всички биха били в един).
            var differentIp = await PostLoginAsync(client, "5.6.7.8");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, differentIp.StatusCode);
        }

        [Fact]
        public async Task ReverseProxyDisabled_ForwardedForIsIgnored_AllRequestsShareOneBucket()
        {
            // Контролен тест: без ForwardedHeaders middleware X-Forwarded-For е просто
            // безобиден header — 11-ата заявка от „различен" IP пак е 429.
            using var factory = new PetCareWebApplicationFactory(_fixture.ConnectionString, "Development");
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false
            });

            for (var i = 0; i < 10; i++)
            {
                var response = await PostLoginAsync(client, "1.2.3.4");
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            }

            var eleventhDifferentIp = await PostLoginAsync(client, "5.6.7.8");
            Assert.Equal(HttpStatusCode.TooManyRequests, eleventhDifferentIp.StatusCode);
        }

        private PetCareWebApplicationFactory CreateForwardedHeadersFactory() =>
            new(_fixture.ConnectionString, "Development", new Dictionary<string, string?>
            {
                ["ReverseProxy:Enabled"] = "true"
            });

        private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string? forwardedFor = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Email"] = "rate-limit@test.bg",
                    ["Password"] = "Password1"
                })
            };

            if (forwardedFor != null)
            {
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
            }

            return client.SendAsync(request);
        }
    }
}
