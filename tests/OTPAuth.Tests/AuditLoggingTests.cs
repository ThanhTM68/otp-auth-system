using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OTPAuth.API.Configuration;
using OTPAuth.API.Data;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

public class AuditLoggingTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] OtpHashingKey = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
    private static readonly byte[] JwtSigningKey = Enumerable.Range(32, 32).Select(index => (byte)index).ToArray();

    [Fact]
    public async Task Login_RecordsSuccessFailureAndOtpCreatedWithoutSensitiveFields()
    {
        await using var context = CreateContext();
        var user = await AddUserAsync(context, "student@example.com", "ValidPassword123!");
        var service = CreateAuthService(context, new FakeEmailService());

        var failed = await service.LoginAsync(new LoginRequest
        {
            Email = user.Email,
            Password = "WrongPassword123!"
        });
        var successful = await service.LoginAsync(new LoginRequest
        {
            Email = user.Email,
            Password = "ValidPassword123!"
        });

        var events = await context.AuditLogs.ToListAsync();
        Assert.Equal(LoginStatus.InvalidCredentials, failed.Status);
        Assert.Equal(LoginStatus.Success, successful.Status);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.LoginPasswordFailed && !audit.Success && audit.UserId == user.Id);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.LoginPasswordSuccess && audit.Success && audit.UserId == user.Id);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.OtpCreated && audit.Success && audit.UserId == user.Id);
        AssertAuditLogHasNoSensitiveProperties();
    }

    [Fact]
    public async Task VerifyOtp_RecordsFailureSuccessJwtAndReplayWithoutOtpValues()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context, "student@example.com", "ValidPassword123!");
        var challenge = await AddChallengeAsync(context, otpService, user, "004821");
        var service = CreateAuthService(context, new FakeEmailService(), otpService);

        var wrong = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004822" });
        var correct = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });
        var replay = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });

        var events = await context.AuditLogs.Where(audit => audit.OtpChallengeId == challenge.Id).ToListAsync();
        Assert.Equal(VerifyOtpStatus.VerificationFailed, wrong.Status);
        Assert.Equal(VerifyOtpStatus.Success, correct.Status);
        Assert.Equal(VerifyOtpStatus.VerificationFailed, replay.Status);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.OtpVerifyFailed && audit.ReasonCode == AuditReasonCodes.OtpMismatch);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.OtpVerifySuccess && audit.Success);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.JwtIssued && audit.Success);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.OtpReplayRejected && !audit.Success);
        Assert.DoesNotContain(events, audit => audit.EventType.Contains("004821", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resend_RecordsSuccessAndCooldownFailure()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context, "student@example.com", "ValidPassword123!");
        var challenge = await AddChallengeAsync(context, otpService, user, "004821", FixedNow.AddSeconds(-60));
        var service = CreateAuthService(context, new FakeEmailService(), otpService);

        var success = await service.ResendOtpAsync(new ResendOtpRequest { ChallengeId = challenge.Id });
        var replacement = await context.OtpChallenges.SingleAsync(item => item.Id != challenge.Id);
        var cooldown = await service.ResendOtpAsync(new ResendOtpRequest { ChallengeId = replacement.Id });

        var events = await context.AuditLogs.ToListAsync();
        Assert.Equal(ResendOtpStatus.Success, success.Status);
        Assert.Equal(ResendOtpStatus.Cooldown, cooldown.Status);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.OtpResendSuccess && audit.OtpChallengeId == replacement.Id);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.OtpCreated && audit.OtpChallengeId == replacement.Id);
        Assert.Contains(events, audit => audit.EventType == AuditEventTypes.OtpResendFailed && audit.ReasonCode == AuditReasonCodes.ResendCooldown);
    }

    [Fact]
    public async Task AuditService_UsesServerMetadataWithLengthLimits()
    {
        await using var context = CreateContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2001:db8::1");
        httpContext.Request.Headers.UserAgent = new string('a', 300);
        httpContext.TraceIdentifier = new string('b', 80);
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var service = new AuditService(
            context,
            accessor,
            new FixedTimeProvider(FixedNow),
            NullLogger<AuditService>.Instance);

        service.Record(new AuditEvent(AuditEventTypes.LoginPasswordFailed, false, ReasonCode: AuditReasonCodes.InvalidCredentials));
        await context.SaveChangesAsync();

        var audit = await context.AuditLogs.SingleAsync();
        Assert.Equal("2001:db8::1", audit.IpAddress);
        Assert.Equal(256, audit.UserAgent!.Length);
        Assert.Equal(64, audit.CorrelationId!.Length);
        Assert.Equal(FixedNow, audit.CreatedAt);
        AssertAuditLogHasNoSensitiveProperties();
    }

    private static void AssertAuditLogHasNoSensitiveProperties()
    {
        var propertyNames = typeof(AuditLog).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("OtpHash", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Jwt", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
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
        IEmailService emailService,
        OtpService? otpService = null) =>
        new(
            context,
            new PasswordHasher<User>(),
            new FixedTimeProvider(FixedNow),
            otpService ?? CreateOtpService(),
            emailService,
            new JwtTokenService(new JwtOptions
            {
                Issuer = "OTPAuth.API.Tests",
                Audience = "OTPAuth.Client.Tests",
                SigningKey = Convert.ToBase64String(JwtSigningKey),
                ExpirationMinutes = 15
            }, JwtSigningKey),
            new FakeAuditService(context));

    private static OtpService CreateOtpService() =>
        new(Options.Create(new OtpOptions()), OtpHashingKey);

    private static async Task<User> AddUserAsync(AppDbContext context, string email, string password)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = EmailNormalizer.Normalize(email),
            FullName = "Nguyen Van A",
            IsActive = true,
            CreatedAt = FixedNow.AddMinutes(-1)
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<OtpChallenge> AddChallengeAsync(
        AppDbContext context,
        OtpService otpService,
        User user,
        string otp,
        DateTimeOffset? createdAt = null)
    {
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthenticationFlowId = Guid.NewGuid(),
            Purpose = "LOGIN",
            CreatedAt = createdAt ?? FixedNow.AddMinutes(-1),
            ExpiresAt = FixedNow.AddMinutes(2),
            FlowExpiresAt = FixedNow.AddMinutes(9),
            MaxAttempts = 5,
            ResendCount = 0,
            IsRevoked = false
        };
        challenge.OtpHash = otpService.HashOtp(challenge, otp);
        context.OtpChallenges.Add(challenge);
        await context.SaveChangesAsync();
        return challenge;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
