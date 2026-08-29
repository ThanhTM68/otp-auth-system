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
        Assert.Contains("Hệ thống xác thực OTP", html, StringComparison.Ordinal);
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

        Assert.Contains("<form id=\"login-form\" method=\"post\" action=\"/api/auth/login\" novalidate>", html, StringComparison.Ordinal);
        Assert.Contains("<form id=\"register-form\" method=\"post\" action=\"/api/auth/register\" novalidate>", html, StringComparison.Ordinal);
        Assert.Contains("<form id=\"otp-form\" method=\"post\" action=\"/api/auth/verify-otp\" novalidate>", html, StringComparison.Ordinal);
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
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_UsesFriendlyVietnameseCopyWithoutTechnicalStatusText()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Đăng nhập để tiếp tục sử dụng hệ thống.", html, StringComparison.Ordinal);
        Assert.Contains("Điền thông tin bên dưới để tạo tài khoản mới.", html, StringComparison.Ordinal);
        Assert.Contains("Xác thực đăng nhập", html, StringComparison.Ordinal);
        Assert.Contains("Mã sẽ hết hạn sau", html, StringComparison.Ordinal);
        Assert.Contains("Gửi lại mã", html, StringComparison.Ordinal);
        Assert.Contains("Kiểm tra phiên đăng nhập", html, StringComparison.Ordinal);
        Assert.Contains("Đang tạo tài khoản...", html, StringComparison.Ordinal);
        Assert.Contains("Đang đăng nhập...", html, StringComparison.Ordinal);
        Assert.Contains("Đang xác thực...", html, StringComparison.Ordinal);
        Assert.Contains("Đang gửi mã mới...", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">JWT<", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer token", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMTP", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL Server", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("200 OK", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unsafe-inline", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unsafe-eval", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Root_ProvidesAccessiblePasswordAndOtpInputs()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("data-password-toggle=\"login-password\"", html, StringComparison.Ordinal);
        Assert.Contains("data-password-toggle=\"register-password\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"otp-input\" class=\"otp-input\" name=\"otp\" type=\"text\"", html, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"numeric\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"one-time-code\"", html, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"6\"", html, StringComparison.Ordinal);
        Assert.Contains("pattern=\"[0-9]{6}\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"otp-input-error\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrontendScript_PreservesOtpAsStringAndUsesFriendlyStates()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/app.js");

        Assert.Contains("const otp = otpInput.value;", script, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify({ challengeId: state.challengeId, otp })", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Number(otp", script, StringComparison.Ordinal);
        Assert.DoesNotContain("parseInt(otp", script, StringComparison.Ordinal);
        Assert.Contains("Email hoặc mật khẩu không chính xác.", script, StringComparison.Ordinal);
        Assert.Contains("Mật khẩu nhập lại không khớp.", script, StringComparison.Ordinal);
        Assert.Contains("Mã xác thực mới đã được gửi đến email của bạn.", script, StringComparison.Ordinal);
        Assert.Contains("Phiên đăng nhập đang hoạt động.", script, StringComparison.Ordinal);
        Assert.Contains("Bạn đã đăng xuất.", script, StringComparison.Ordinal);
        Assert.Contains("sessionGeneration", script, StringComparison.Ordinal);
        Assert.Contains("otpActionInProgress", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Styles_IncludeResponsiveAndVisibleFocusStates()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var styles = await client.GetStringAsync("/styles.css");

        Assert.Contains("@media (max-width: 850px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 520px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 380px)", styles, StringComparison.Ordinal);
        Assert.Contains("button:focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("a:focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", styles, StringComparison.Ordinal);
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
