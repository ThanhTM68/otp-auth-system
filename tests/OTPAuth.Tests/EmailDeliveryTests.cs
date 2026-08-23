using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OTPAuth.API.Configuration;
using OTPAuth.API.Data;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

public class EmailDeliveryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidLogin_CreatesChallengeAndDeliversMatchingOtpOnce()
    {
        await using var context = CreateContext();
        var user = await AddUserAsync(context, "student@example.com", "ValidPassword123!");
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService();
        var authService = CreateAuthService(context, otpService, emailService);

        var result = await authService.LoginAsync(new LoginRequest
        {
            Email = user.Email,
            Password = "ValidPassword123!"
        });

        var challenge = await context.OtpChallenges.SingleAsync();
        var email = Assert.Single(emailService.Messages);

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(1, emailService.CallCount);
        Assert.Equal(user.Email, email.RecipientEmail);
        Assert.Matches(new Regex("^[0-9]{6}$"), email.Otp);
        Assert.Equal(challenge.ExpiresAt, email.ExpiresAt);
        Assert.True(otpService.VerifyOtp(challenge, email.Otp));
        Assert.Null(result.Response!.GetType().GetProperty("Otp"));
        Assert.Null(result.Response.GetType().GetProperty("AccessToken"));
    }

    [Fact]
    public async Task EmailDeliveryFailure_RevokesChallengeAndDoesNotReturnSuccess()
    {
        await using var context = CreateContext();
        await AddUserAsync(context, "student@example.com", "ValidPassword123!");
        var emailService = new FakeEmailService(shouldFail: true);
        var authService = CreateAuthService(context, CreateOtpService(), emailService);

        var result = await authService.LoginAsync(new LoginRequest
        {
            Email = "student@example.com",
            Password = "ValidPassword123!"
        });

        var challenge = await context.OtpChallenges.SingleAsync();

        Assert.Equal(LoginStatus.EmailDeliveryFailure, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(1, emailService.CallCount);
        Assert.True(challenge.IsRevoked);
    }

    [Fact]
    public async Task Login_WhenOtpExpiresDuringEmailDelivery_RevokesChallengeAndDoesNotReturnSuccess()
    {
        await using var context = CreateContext();
        await AddUserAsync(context, "student@example.com", "ValidPassword123!");
        var emailService = new FakeEmailService();
        var timeProvider = new SequenceTimeProvider(FixedNow, FixedNow.AddMinutes(3));
        var authService = CreateAuthService(context, CreateOtpService(), emailService, timeProvider);

        var result = await authService.LoginAsync(new LoginRequest
        {
            Email = "student@example.com",
            Password = "ValidPassword123!"
        });

        var challenge = await context.OtpChallenges.SingleAsync();

        Assert.Equal(LoginStatus.EmailDeliveryFailure, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(1, emailService.CallCount);
        Assert.True(challenge.IsRevoked);
    }

    [Theory]
    [InlineData("student@example.com", "WrongPassword123!")]
    [InlineData("unknown@example.com", "ValidPassword123!")]
    public async Task InvalidCredentials_DoNotCreateChallengeOrSendEmail(string email, string password)
    {
        await using var context = CreateContext();
        await AddUserAsync(context, "student@example.com", "ValidPassword123!");
        var emailService = new FakeEmailService();
        var authService = CreateAuthService(context, CreateOtpService(), emailService);

        var result = await authService.LoginAsync(new LoginRequest { Email = email, Password = password });

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Empty(context.OtpChallenges);
        Assert.Equal(0, emailService.CallCount);
    }

    [Fact]
    public void Template_ContainsOnlyOtpAndExpiryInstructions()
    {
        var body = EmailService.BuildOtpBody(new OtpEmailMessage(
            "student@example.com",
            "004821",
            new DateTimeOffset(2026, 8, 23, 10, 3, 0, TimeSpan.Zero)));

        Assert.Contains("004821", body);
        Assert.Contains("Mã có hiệu lực trong 3 phút", body);
        Assert.Contains("UTC", body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OtpHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JWT", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmailService_RejectsMissingSmtpConfigurationWithoutConnecting()
    {
        var emailService = new EmailService(
            Options.Create(new EmailOptions()),
            NullLogger<EmailService>.Instance);

        await Assert.ThrowsAsync<EmailDeliveryException>(() => emailService.SendOtpAsync(
            new OtpEmailMessage(
                "student@example.com",
                "004821",
                DateTimeOffset.UtcNow.AddMinutes(3))));
    }

    [Fact]
    public async Task EmailService_LogsSafeMissingConfigurationDiagnostic()
    {
        var logger = new CapturingLogger<EmailService>();
        var emailService = new EmailService(
            Options.Create(new EmailOptions
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true
            }),
            logger);

        await Assert.ThrowsAsync<EmailDeliveryException>(() => emailService.SendOtpAsync(
            new OtpEmailMessage(
                "student@example.com",
                "004821",
                DateTimeOffset.UtcNow.AddMinutes(3))));

        var diagnostic = Assert.Single(logger.Messages);
        Assert.Contains("Username", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Password", diagnostic, StringComparison.Ordinal);
        Assert.Contains("FromEmail", diagnostic, StringComparison.Ordinal);
        Assert.Contains("smtp.gmail.com", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("004821", diagnostic, StringComparison.Ordinal);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AuthService CreateAuthService(
        AppDbContext context,
        OtpService otpService,
        IEmailService emailService,
        TimeProvider? timeProvider = null) =>
        new(context, new PasswordHasher<User>(), timeProvider ?? TimeProvider.System, otpService, emailService, new FakeJwtTokenService(), new FakeAuditService(context));

    private static OtpService CreateOtpService() =>
        new(Options.Create(new OtpOptions()), Enumerable.Range(0, 32).Select(index => (byte)index).ToArray());

    private static async Task<User> AddUserAsync(AppDbContext context, string email, string password)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = EmailNormalizer.Normalize(email),
            FullName = "Nguyen Van A",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private sealed class SequenceTimeProvider(params DateTimeOffset[] timestamps) : TimeProvider
    {
        private int currentIndex;

        public override DateTimeOffset GetUtcNow()
        {
            var index = Math.Min(currentIndex, timestamps.Length - 1);
            currentIndex++;
            return timestamps[index];
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
