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
}
