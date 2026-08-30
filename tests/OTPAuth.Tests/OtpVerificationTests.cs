using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OTPAuth.API.Configuration;
using OTPAuth.API.Data;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

public class OtpVerificationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] OtpHashingKey = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
    private static readonly byte[] JwtSigningKey = Enumerable.Range(32, 32).Select(index => (byte)index).ToArray();

    [Fact]
    public async Task ValidOtp_ConsumesChallengeAndCreatesValidJwt()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821");
        var service = CreateAuthService(context, otpService);

        var result = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });

        var persistedChallenge = await context.OtpChallenges.SingleAsync();

        Assert.Equal(VerifyOtpStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(FixedNow, persistedChallenge.ConsumedAt);
        Assert.True(ValidateToken(result.Response!.AccessToken, user.Id));
        Assert.Equal("Bearer", result.Response.TokenType);
        Assert.Equal(900, result.Response.ExpiresIn);
    }

    [Fact]
    public async Task VerifyOtpTwice_ShouldFailSecondAttempt()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821");
        var service = CreateAuthService(context, otpService);

        var first = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });
        var second = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });

        Assert.Equal(VerifyOtpStatus.Success, first.Status);
        Assert.Equal(VerifyOtpStatus.NotCurrent, second.Status);
        Assert.Null(second.Response);
        Assert.NotNull((await context.OtpChallenges.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task WrongOtp_IncrementsAttemptAndDoesNotCreateJwt()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821");

        var result = await CreateAuthService(context, otpService).VerifyOtpAsync(
            new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004822" });

        var persistedChallenge = await context.OtpChallenges.SingleAsync();
        Assert.Equal(VerifyOtpStatus.VerificationFailed, result.Status);
        Assert.Null(result.Response);
        Assert.Equal((short)1, persistedChallenge.AttemptCount);
        Assert.Null(persistedChallenge.ConsumedAt);
        Assert.False(persistedChallenge.IsRevoked);
    }

    [Fact]
    public async Task FifthWrongOtp_RevokesChallengeAndCorrectOtpIsRejected()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821", attemptCount: 4);
        var service = CreateAuthService(context, otpService);

        var wrong = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004822" });
        var correctAfterLock = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });

        var persistedChallenge = await context.OtpChallenges.SingleAsync();
        Assert.Equal(VerifyOtpStatus.MaxAttempts, wrong.Status);
        Assert.Equal((short)5, persistedChallenge.AttemptCount);
        Assert.True(persistedChallenge.IsRevoked);
        Assert.Equal(VerifyOtpStatus.MaxAttempts, correctAfterLock.Status);
        Assert.Null(correctAfterLock.Response);
    }

    [Fact]
    public async Task ExpiredOtp_IsRejectedWithoutConsumeOrJwt()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821", expiresAt: FixedNow);

        var result = await CreateAuthService(context, otpService).VerifyOtpAsync(
            new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });

        Assert.Equal(VerifyOtpStatus.Expired, result.Status);
        Assert.Null(result.Response);
        Assert.Null((await context.OtpChallenges.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task OtpThatExpiresDuringVerification_IsRejectedWithoutConsumeOrJwt()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(
            context,
            otpService,
            user,
            "004821",
            expiresAt: FixedNow.AddMinutes(2));
        var timeProvider = new SequenceTimeProvider(FixedNow, challenge.ExpiresAt!.Value);

        var result = await CreateAuthService(context, otpService, timeProvider).VerifyOtpAsync(
            new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });

        var persistedChallenge = await context.OtpChallenges.SingleAsync();
        Assert.Equal(VerifyOtpStatus.Expired, result.Status);
        Assert.Null(result.Response);
        Assert.Null(persistedChallenge.ConsumedAt);
        Assert.Equal((short)0, persistedChallenge.AttemptCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RevokedOrConsumedOtp_IsRejected(bool isRevoked, bool isConsumed)
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var user = await AddUserAsync(context);
        var challenge = await AddChallengeAsync(context, otpService, user, "004821");
        challenge.IsRevoked = isRevoked;
        challenge.ConsumedAt = isConsumed ? FixedNow.AddMinutes(-1) : null;
        await context.SaveChangesAsync();

        var result = await CreateAuthService(context, otpService).VerifyOtpAsync(
            new VerifyOtpRequest { ChallengeId = challenge.Id, Otp = "004821" });

        Assert.Equal(VerifyOtpStatus.NotCurrent, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task VerifyBeforeOtpWasSent_IsRejectedWithoutAttemptOrJwt()
    {
        await using var context = CreateContext();
        var user = await AddUserAsync(context);
        var pending = CreateOtpService().CreatePendingLoginChallenge(user, FixedNow.AddMinutes(-1));
        context.OtpChallenges.Add(pending);
        await context.SaveChangesAsync();
        var jwtTokenService = new FakeJwtTokenService();
        var service = CreateAuthService(context, CreateOtpService(), jwtTokenService: jwtTokenService);

        var result = await service.VerifyOtpAsync(
            new VerifyOtpRequest { ChallengeId = pending.Id, Otp = "004821" });

        Assert.Equal(VerifyOtpStatus.NotSent, result.Status);
        Assert.Null(result.Response);
        Assert.Equal((short)0, pending.AttemptCount);
        Assert.Null(pending.ConsumedAt);
        Assert.Equal(0, jwtTokenService.CallCount);
    }

    [Fact]
    public async Task MissingChallenge_WrongPurposeAndInactiveUser_DoNotCreateJwt()
    {
        await using var context = CreateContext();
        var otpService = CreateOtpService();
        var activeUser = await AddUserAsync(context);
        var inactiveUser = await AddUserAsync(context, "inactive@example.com", isActive: false);
        var wrongPurposeChallenge = await AddChallengeAsync(context, otpService, activeUser, "004821", purpose: "OTHER");
        var inactiveChallenge = await AddChallengeAsync(context, otpService, inactiveUser, "004821");
        var service = CreateAuthService(context, otpService);

        var missing = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = Guid.NewGuid(), Otp = "004821" });
        var wrongPurpose = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = wrongPurposeChallenge.Id, Otp = "004821" });
        var inactive = await service.VerifyOtpAsync(new VerifyOtpRequest { ChallengeId = inactiveChallenge.Id, Otp = "004821" });

        Assert.Equal(VerifyOtpStatus.VerificationFailed, missing.Status);
        Assert.Null(missing.Response);
        Assert.Equal(VerifyOtpStatus.VerificationFailed, wrongPurpose.Status);
        Assert.Null(wrongPurpose.Response);
        Assert.Equal(VerifyOtpStatus.VerificationFailed, inactive.Status);
        Assert.Null(inactive.Response);
    }

    [Fact]
    public void VerifyOtpRequest_RejectsInvalidChallengeOrOtpFormat()
    {
        var request = new VerifyOtpRequest { ChallengeId = null, Otp = " 4821 " };
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
        TimeProvider? timeProvider = null,
        IJwtTokenService? jwtTokenService = null) =>
        new(
            context,
            new PasswordHasher<User>(),
            timeProvider ?? new FixedTimeProvider(FixedNow),
            otpService,
            new FakeEmailService(),
            jwtTokenService ?? new JwtTokenService(CreateJwtOptions(), JwtSigningKey),
            new FakeAuditService(context));

    private static OtpService CreateOtpService() =>
        new(Microsoft.Extensions.Options.Options.Create(new OtpOptions()), OtpHashingKey);

    private static JwtOptions CreateJwtOptions() => new()
    {
        Issuer = "OTPAuth.API.Tests",
        Audience = "OTPAuth.Client.Tests",
        SigningKey = Convert.ToBase64String(JwtSigningKey),
        ExpirationMinutes = 15
    };

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
            CreatedAt = FixedNow.AddMinutes(-1)
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
        short attemptCount = 0,
        DateTimeOffset? expiresAt = null,
        string purpose = "LOGIN")
    {
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthenticationFlowId = Guid.NewGuid(),
            Purpose = purpose,
            CreatedAt = FixedNow.AddMinutes(-1),
            SentAt = FixedNow.AddMinutes(-1),
            ExpiresAt = expiresAt ?? FixedNow.AddMinutes(2),
            FlowExpiresAt = FixedNow.AddMinutes(9),
            AttemptCount = attemptCount,
            MaxAttempts = 5,
            ResendCount = 0,
            IsRevoked = false
        };
        challenge.OtpHash = otpService.HashOtp(challenge, otp);
        context.OtpChallenges.Add(challenge);
        await context.SaveChangesAsync();
        return challenge;
    }

    private static bool ValidateToken(string token, Guid expectedUserId)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(JwtSigningKey),
            ValidateIssuer = true,
            ValidIssuer = "OTPAuth.API.Tests",
            ValidateAudience = true,
            ValidAudience = "OTPAuth.Client.Tests",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, _) =>
                notBefore <= FixedNow.UtcDateTime && expires > FixedNow.UtcDateTime
        }, out var validatedToken);

        Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(expectedUserId.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.DoesNotContain(principal.Claims, claim =>
            claim.Type is "password" or "passwordHash" or "otp" or "otpHash" or "signingKey");
        return true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
}
