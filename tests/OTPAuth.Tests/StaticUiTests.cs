using System.Net;

namespace OTPAuth.Tests;

[Collection("Environment variable tests")]
public class StaticUiTests
{
    [Fact]
    public async Task Root_ReturnsOtpDemoWithoutSensitiveServerData()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("OTP Authentication Demo", html, StringComparison.Ordinal);
        Assert.Contains("register-form", html, StringComparison.Ordinal);
        Assert.Contains("login-form", html, StringComparison.Ordinal);
        Assert.Contains("otp-form", html, StringComparison.Ordinal);
        Assert.Contains("dashboard", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SigningKey", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OtpHash", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings", html, StringComparison.OrdinalIgnoreCase);
        AssertHeaderContains(response, "Content-Security-Policy", "frame-ancestors 'none'");
        AssertHeaderContains(response, "X-Frame-Options", "DENY");
        AssertHeaderContains(response, "X-Content-Type-Options", "nosniff");
        AssertHeaderContains(response, "Referrer-Policy", "no-referrer");
    }

    [Fact]
    public async Task SensitiveForms_UsePostFallbackWithoutPuttingCredentialsInUrl()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<form id=\"login-form\" method=\"post\" action=\"/api/auth/login\">", html, StringComparison.Ordinal);
        Assert.Contains("<form id=\"register-form\" method=\"post\" action=\"/api/auth/register\">", html, StringComparison.Ordinal);
        Assert.Contains("<form id=\"otp-form\" method=\"post\" action=\"/api/auth/verify-otp\">", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/app.js", "javascript")]
    public async Task StaticAsset_IsServed(string path, string expectedContentType)
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedContentType, response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FrontendScript_UsesSessionTokenWithoutLoggingSensitiveData()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/app.js");

        Assert.Contains("sessionStorage", script, StringComparison.Ordinal);
        Assert.Contains("challengeId", script, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("console.", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SigningKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HashingKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings", script, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertHeaderContains(
        HttpResponseMessage response,
        string headerName,
        string expectedValue)
    {
        Assert.True(response.Headers.TryGetValues(headerName, out var values));
        Assert.Contains(expectedValue, string.Join(",", values), StringComparison.OrdinalIgnoreCase);
    }
}
