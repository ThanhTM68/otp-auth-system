using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OTPAuth.API.Configuration;
using OTPAuth.API.Data;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

public class ResendOtpTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] OtpHashingKey = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
    private static readonly byte[] JwtSigningKey = Enumerable.Range(32, 32).Select(index => (byte)index).ToArray();

    [Fact]
    public async Task EligibleResend_RevokesOldChallengeAndNewOtpCanBeVerified()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService();
        var user = await AddUserAsync(context);
        var oldChallenge = await AddChallengeAsync(context, otpService, user, "004821", FixedNow.AddSeconds(-60));
        var service = CreateAuthService(context, otpService, emailService);

        var resend = await service.ResendOtpAsync(new ResendOtpRequest { ChallengeId = oldChallenge.Id });

        var challenges = await context.OtpChallenges.OrderBy(challenge => challenge.CreatedAt).ToListAsync();
        var newChallenge = Assert.Single(challenges, challenge => challenge.Id != oldChallenge.Id);
        Assert.Equal(ResendOtpStatus.Success, resend.Status);
        Assert.NotNull(resend.Response);
        Assert.True(challenges.Single(challenge => challenge.Id == oldChallenge.Id).IsRevoked);
        Assert.False(newChallenge.IsRevoked);
        Assert.Null(newChallenge.ConsumedAt);
        Assert.Equal((short)0, newChallenge.AttemptCount);
        Assert.Equal((short)1, newChallenge.ResendCount);
        Assert.Equal(oldChallenge.AuthenticationFlowId, newChallenge.AuthenticationFlowId);
        Assert.Equal(32, newChallenge.OtpHash.Length);
        Assert.Equal(1, emailService.CallCount);
        Assert.Equal(user.Email, Assert.Single(emailService.Messages).RecipientEmail);
        Assert.Equal(newChallenge.Id, resend.Response!.ChallengeId);
        Assert.Null(typeof(ResendOtpResponse).GetProperty("AccessToken"));

        var oldOtp = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = oldChallenge.Id, Otp = "004821" });
        var newOtp = await service.VerifyOtpAsync(new VerifyOtpRequest
        {
            ChallengeId = newChallenge.Id,
            Otp = emailService.Messages[0].Otp
        });

        Assert.Equal(VerifyOtpStatus.VerificationFailed, oldOtp.Status);
        Assert.Null(oldOtp.Response);
        Assert.Equal(VerifyOtpStatus.Success, newOtp.Status);
        Assert.NotNull(newOtp.Response);
    }

    [Fact]
    public async Task ResendBeforeCooldown_IsRejectedWithoutEmailOrNewChallenge()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821", FixedNow.AddSeconds(-59));

        var result = await CreateAuthService(context, otpService, emailService).ResendOtpAsync(
            new ResendOtpRequest { ChallengeId = challenge.Id });

        Assert.Equal(ResendOtpStatus.Cooldown, result.Status);
        Assert.Equal(1, result.RetryAfterSeconds);
        Assert.Single(context.OtpChallenges);
        Assert.False((await context.OtpChallenges.SingleAsync()).IsRevoked);
        Assert.Equal(0, emailService.CallCount);
    }

    [Fact]
    public async Task ResendAtCooldownBoundary_IsAllowed()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821", FixedNow.AddSeconds(-60));

        var result = await CreateAuthService(context, otpService, emailService).ResendOtpAsync(
            new ResendOtpRequest { ChallengeId = challenge.Id });

        Assert.Equal(ResendOtpStatus.Success, result.Status);
        Assert.Equal(2, await context.OtpChallenges.CountAsync());
        Assert.Equal(1, emailService.CallCount);
    }

    [Fact]
    public async Task ConsumedMissingInactiveOrRevokedChallenge_CannotBeResent()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService();
        var activeUser = await AddUserAsync(context);
        var inactiveUser = await AddUserAsync(context, "inactive@example.com", false);
        var consumed = await AddChallengeAsync(context, otpService, activeUser, "004821", FixedNow.AddMinutes(-1));
        var inactive = await AddChallengeAsync(context, otpService, inactiveUser, "004821", FixedNow.AddMinutes(-1));
        var revoked = await AddChallengeAsync(context, otpService, activeUser, "004822", FixedNow.AddMinutes(-1));
        consumed.ConsumedAt = FixedNow.AddSeconds(-1);
        revoked.IsRevoked = true;
        await context.SaveChangesAsync();
        var service = CreateAuthService(context, otpService, emailService);

        var missingResult = await service.ResendOtpAsync(new ResendOtpRequest { ChallengeId = Guid.NewGuid() });
        var consumedResult = await service.ResendOtpAsync(new ResendOtpRequest { ChallengeId = consumed.Id });
        var inactiveResult = await service.ResendOtpAsync(new ResendOtpRequest { ChallengeId = inactive.Id });
        var revokedResult = await service.ResendOtpAsync(new ResendOtpRequest { ChallengeId = revoked.Id });

        Assert.Equal(ResendOtpStatus.NotAvailable, missingResult.Status);
        Assert.Equal(ResendOtpStatus.NotAvailable, consumedResult.Status);
        Assert.Equal(ResendOtpStatus.NotAvailable, inactiveResult.Status);
        Assert.Equal(ResendOtpStatus.NotAvailable, revokedResult.Status);
        Assert.Equal(3, await context.OtpChallenges.CountAsync());
        Assert.Equal(0, emailService.CallCount);
    }

    [Fact]
    public async Task Resend_ResetsAttemptsAndAllowsAnExpiredOtpWithinTheAuthenticationFlow()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService();
        var user = await AddUserAsync(context);
        var oldChallenge = await AddChallengeAsync(
            context,
            otpService,
            user,
            "004821",
            FixedNow.AddMinutes(-2),
            attemptCount: 3,
            expiresAt: FixedNow.AddSeconds(-1));

        var result = await CreateAuthService(context, otpService, emailService).ResendOtpAsync(
            new ResendOtpRequest { ChallengeId = oldChallenge.Id });

        var newChallenge = await context.OtpChallenges.SingleAsync(challenge => challenge.Id != oldChallenge.Id);
        Assert.Equal(ResendOtpStatus.Success, result.Status);
        Assert.True((await context.OtpChallenges.SingleAsync(challenge => challenge.Id == oldChallenge.Id)).IsRevoked);
        Assert.Equal((short)0, newChallenge.AttemptCount);
        Assert.True(newChallenge.ExpiresAt > newChallenge.CreatedAt);
        Assert.Equal(1, emailService.CallCount);
    }

    [Theory]
    [InlineData((short)3, false)]
    [InlineData((short)0, true)]
    public async Task ResendAfterMaxResendsOrFlowExpiry_IsRejected(short resendCount, bool expiredFlow)
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(
            context,
            otpService,
            user,
            "004821",
            FixedNow.AddMinutes(-2),
            expiresAt: expiredFlow ? FixedNow : null,
            resendCount: resendCount,
            flowExpiresAt: expiredFlow ? FixedNow : FixedNow.AddMinutes(8));

        var result = await CreateAuthService(context, otpService, emailService).ResendOtpAsync(
            new ResendOtpRequest { ChallengeId = challenge.Id });

        Assert.Equal(ResendOtpStatus.NotAvailable, result.Status);
        Assert.Single(context.OtpChallenges);
        Assert.Equal(0, emailService.CallCount);
    }

    [Fact]
    public async Task EmailFailure_RevokesTheNewChallengeAndDoesNotRestoreTheOldOne()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var emailService = new FakeEmailService(shouldFail: true);
        var user = await AddUserAsync(context);
        var oldChallenge = await AddChallengeAsync(context, otpService, user, "004821", FixedNow.AddMinutes(-1));

        var result = await CreateAuthService(context, otpService, emailService).ResendOtpAsync(
            new ResendOtpRequest { ChallengeId = oldChallenge.Id });

        var challenges = await context.OtpChallenges.ToListAsync();
        Assert.Equal(ResendOtpStatus.EmailDeliveryFailure, result.Status);
        Assert.Equal(2, challenges.Count);
        Assert.All(challenges, challenge => Assert.True(challenge.IsRevoked));
        Assert.Equal(1, emailService.CallCount);
    }

    [Fact]
    public void ResendOtpRequest_RequiresChallengeId()
    {
        var request = new ResendOtpRequest { ChallengeId = null };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true);

        Assert.False(isValid);
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
        IEmailService emailService) =>
        new(
            context,
            new PasswordHasher<User>(),
            new FixedTimeProvider(FixedNow),
            otpService,
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

    private static async Task<User> AddUserAsync(
        AppDbContext context,
        string email = "student@example.com",
        bool isActive = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = EmailNormalizer.Normalize(email),
            FullName = "Nguyen Van A",
            PasswordHash = "test-only-hash",
            IsActive = isActive,
            CreatedAt = FixedNow.AddMinutes(-3)
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<OtpChallenge> AddChallengeAsync(
        AppDbContext context,
        OtpService otpService,
        User user,
        string otp,
        DateTimeOffset createdAt,
        short attemptCount = 0,
        DateTimeOffset? expiresAt = null,
        short resendCount = 0,
        DateTimeOffset? flowExpiresAt = null)
    {
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthenticationFlowId = Guid.NewGuid(),
            Purpose = "LOGIN",
            CreatedAt = createdAt,
            ExpiresAt = expiresAt ?? createdAt.AddMinutes(3),
            FlowExpiresAt = flowExpiresAt ?? FixedNow.AddMinutes(8),
            AttemptCount = attemptCount,
            MaxAttempts = 5,
            ResendCount = resendCount,
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
