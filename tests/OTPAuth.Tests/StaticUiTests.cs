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
    [InlineData("/assets/otp-logo.jpeg", "image/jpeg")]
    [InlineData("/assets/otp-background.jpeg", "image/jpeg")]
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
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", script, StringComparison.Ordinal);
        Assert.Equal(1, script.Split("sessionStorage.setItem(", StringSplitOptions.None).Length - 1);
        Assert.Contains("sessionStorage.setItem(tokenStorageKey, result.accessToken)", script, StringComparison.Ordinal);
        Assert.Contains("sessionStorage.removeItem(tokenStorageKey)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_UsesFriendlyVietnameseCopyWithoutTechnicalStatusText()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Bảo vệ tài khoản", html, StringComparison.Ordinal);
        Assert.Contains("bằng xác thực hai bước.", html, StringComparison.Ordinal);
        Assert.Contains("Nhập thông tin tài khoản để tiếp tục.", html, StringComparison.Ordinal);
        Assert.Contains("Chỉ mất một phút để bắt đầu.", html, StringComparison.Ordinal);
        Assert.Contains("Nhập mã xác thực", html, StringComparison.Ordinal);
        Assert.Contains("Mã hết hạn sau", html, StringComparison.Ordinal);
        Assert.Contains("Gửi lại mã", html, StringComparison.Ordinal);
        Assert.Contains("Kiểm tra phiên đăng nhập", html, StringComparison.Ordinal);
        Assert.Contains("Đang tạo tài khoản...", html, StringComparison.Ordinal);
        Assert.Contains("Đang kiểm tra...", html, StringComparison.Ordinal);
        Assert.Contains("Đang xác thực...", html, StringComparison.Ordinal);
        Assert.Contains("Đang gửi mã mới...", html, StringComparison.Ordinal);
        Assert.Contains("Thông tin xác thực được bảo vệ", html, StringComparison.Ordinal);
        Assert.Contains("Mật khẩu và mã OTP không được lưu ở dạng văn bản thuần.", html, StringComparison.Ordinal);
        Assert.Contains("Xác thực thành công", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Bảo vệ từng phiên đăng nhập", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Danh tính số an toàn", html, StringComparison.Ordinal);
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
        Assert.Contains("data-password-toggle=\"register-confirm-password\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Hiện mật khẩu\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"step\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/assets/otp-logo.jpeg\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"otp-expiry-status\"", html, StringComparison.Ordinal);
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
        Assert.Contains("Mã mới đã được gửi.", script, StringComparison.Ordinal);
        Assert.Contains("Phiên đăng nhập đang hoạt động.", script, StringComparison.Ordinal);
        Assert.Contains("Bạn đã đăng xuất.", script, StringComparison.Ordinal);
        Assert.Contains("sessionGeneration", script, StringComparison.Ordinal);
        Assert.Contains("otpActionInProgress", script, StringComparison.Ordinal);
        Assert.Contains("setPasswordToggleState", script, StringComparison.Ordinal);
        Assert.DoesNotContain("button.textContent = shouldShow", script, StringComparison.Ordinal);
        Assert.Contains("item.setAttribute(\"aria-current\", \"step\")", script, StringComparison.Ordinal);
        Assert.Contains("expiryJustAnnounced", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtpView_ProvidesPendingSendingAndSentStates()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var script = await client.GetStringAsync("/app.js");

        Assert.Contains("data-otp-state=\"pending\"", html, StringComparison.Ordinal);
        Assert.Contains("data-otp-state=\"sent\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"send-otp-button\"", html, StringComparison.Ordinal);
        Assert.Contains("Gửi mã xác thực", html, StringComparison.Ordinal);
        Assert.Contains("Đang gửi mã xác thực...", html, StringComparison.Ordinal);
        Assert.Contains("Quá trình này có thể mất vài giây.", html, StringComparison.Ordinal);
        Assert.Contains("Thông tin đăng nhập đã được xác minh.", html, StringComparison.Ordinal);
        Assert.Contains("Mã xác thực đã được gửi đến email của bạn.", script, StringComparison.Ordinal);
        Assert.Contains("sendOtp: \"/api/auth/send-otp\"", script, StringComparison.Ordinal);
        Assert.Contains("sendOtpButton.addEventListener(\"click\"", script, StringComparison.Ordinal);
        Assert.Contains("showOtpState(\"pending\")", script, StringComparison.Ordinal);
        Assert.Contains("showOtpState(\"sent\")", script, StringComparison.Ordinal);
        Assert.Equal(1, script.Split("request(api.sendOtp", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("sessionStorage.setItem(\"challengeId\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Styles_IncludeGlassFallbackResponsiveAndAccessibleMotionStates()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var styles = await client.GetStringAsync("/styles.css");

        Assert.Contains("color-scheme: dark", styles, StringComparison.Ordinal);
        Assert.Contains("background: var(--surface-card-fallback)", styles, StringComparison.Ordinal);
        Assert.Contains("backdrop-filter: blur(26px)", styles, StringComparison.Ordinal);
        Assert.Contains("url(\"/assets/otp-background.jpeg\")", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 520px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 380px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", styles, StringComparison.Ordinal);
        Assert.Contains("button:focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("a:focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", styles, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_PreservesAllFrontendDomHooks()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        string[] requiredIds =
        [
            "alert", "login-form", "login-email", "login-password", "register-form",
            "register-name", "register-email", "register-password", "register-confirm-password",
            "send-otp-button", "otp-send-progress", "pending-otp-destination",
            "otp-form", "otp-input", "otp-destination", "otp-timer-message", "otp-timer-label",
            "otp-countdown", "resend-timer-label", "resend-countdown", "resend-period", "otp-expiry-status",
            "resend-button", "dashboard-greeting", "profile-name", "profile-email",
            "check-profile-button", "logout-button"
        ];

        foreach (var id in requiredIds)
        {
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains("data-view=\"login\"", html, StringComparison.Ordinal);
        Assert.Contains("data-view=\"register\"", html, StringComparison.Ordinal);
        Assert.Contains("data-view=\"otp\"", html, StringComparison.Ordinal);
        Assert.Contains("data-view=\"dashboard\"", html, StringComparison.Ordinal);
        Assert.Equal(3, html.Split("data-flow-step=", StringSplitOptions.None).Length - 1);
        Assert.Contains("data-cancel-otp", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/swagger\"", html, StringComparison.Ordinal);
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
