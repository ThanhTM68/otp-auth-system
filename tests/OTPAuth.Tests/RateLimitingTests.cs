using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OTPAuth.Tests;

[Collection("Environment variable tests")]
public class RateLimitingTests
{
    [Theory]
    [InlineData("/api/auth/register", 5)]
    [InlineData("/api/auth/login", 5)]
    [InlineData("/api/auth/verify-otp", 10)]
    [InlineData("/api/auth/resend-otp", 3)]
    public async Task SensitiveEndpoint_Returns429AfterItsConfiguredLimit(string path, int permitLimit)
    {
        using var factory = CreateFactory();
        using var client = CreateHttpsClient(factory);

        for (var request = 0; request < permitLimit; request++)
        {
            using var allowedResponse = await client.PostAsJsonAsync(path, new { });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowedResponse.StatusCode);
        }

        using var rejectedResponse = await client.PostAsJsonAsync(path, new { });
        var body = await rejectedResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        Assert.Equal("application/problem+json", rejectedResponse.Content.Headers.ContentType?.MediaType);
        Assert.True(rejectedResponse.Headers.Contains("Retry-After"));
        Assert.Contains("RATE_LIMITED", body, StringComparison.Ordinal);
        Assert.DoesNotContain("register", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("login", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verify-otp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resend-otp", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginLimit_DoesNotConsumeVerifyOtpPolicy()
    {
        using var factory = CreateFactory();
        using var client = CreateHttpsClient(factory);

        for (var request = 0; request < 6; request++)
        {
            using var _ = await client.PostAsJsonAsync("/api/auth/login", new { });
        }

        using var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify-otp", new { });

        Assert.Equal(HttpStatusCode.BadRequest, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task LoginLimit_DoesNotConsumeRegisterPolicy()
    {
        using var factory = CreateFactory();
        using var client = CreateHttpsClient(factory);

        for (var request = 0; request < 6; request++)
        {
            using var _ = await client.PostAsJsonAsync("/api/auth/login", new { });
        }

        using var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new { });

        Assert.Equal(HttpStatusCode.BadRequest, registerResponse.StatusCode);
    }

    private static HttpClient CreateHttpsClient(WebApplicationFactory<global::Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static WebApplicationFactory<global::Program> CreateFactory() =>
        new RateLimitingWebApplicationFactory();

    private sealed class RateLimitingWebApplicationFactory : WebApplicationFactory<global::Program>
    {
        private static readonly string OtpTestKey = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray());
        private static readonly string JwtTestKey = Convert.ToBase64String(Enumerable.Repeat((byte)43, 32).ToArray());

        public RateLimitingWebApplicationFactory()
        {
            Environment.SetEnvironmentVariable("Otp__HashingKey", OtpTestKey);
            Environment.SetEnvironmentVariable("Jwt__SigningKey", JwtTestKey);
            Environment.SetEnvironmentVariable("Jwt__Issuer", "OTPAuth.API.Tests");
            Environment.SetEnvironmentVariable("Jwt__Audience", "OTPAuth.Client.Tests");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Environment.SetEnvironmentVariable("Otp__HashingKey", null);
                Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
                Environment.SetEnvironmentVariable("Jwt__Issuer", null);
                Environment.SetEnvironmentVariable("Jwt__Audience", null);
            }

            base.Dispose(disposing);
        }
    }
}
