using System.Text;
using Microsoft.Extensions.Options;
using OTPAuth.API.Configuration;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

public class OtpServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GenerateOtp_ReturnsSixDigits()
    {
        var service = CreateService();

        for (var index = 0; index < 50; index++)
        {
            Assert.Matches("^[0-9]{6}$", service.GenerateOtp());
        }
    }

    [Fact]
    public void FormatOtp_PreservesLeadingZeroes()
    {
        Assert.Equal("004821", OtpService.FormatOtp(4821));
        Assert.Equal("000000", OtpService.FormatOtp(0));
        Assert.Equal("999999", OtpService.FormatOtp(999999));
    }

    [Fact]
    public void HashAndVerifyOtp_UseKeyedHashWithoutPlaintextStorage()
    {
        var service = CreateService();
        var challenge = CreateChallenge();
        const string otp = "004821";

        challenge.OtpHash = service.HashOtp(challenge, otp);

        Assert.Equal(32, challenge.OtpHash.Length);
        Assert.False(challenge.OtpHash.SequenceEqual(Encoding.UTF8.GetBytes(otp)));
        Assert.True(service.VerifyOtp(challenge, otp));
        Assert.False(service.VerifyOtp(challenge, "004822"));
        Assert.Null(typeof(OtpChallenge).GetProperty("Otp"));
    }

    [Fact]
    public void CreateLoginChallenge_InitializesSecureLifecycleState()
    {
        var service = CreateService();
        var challenge = service.CreateLoginChallenge(CreateUser(), FixedNow);

        Assert.Equal("LOGIN", challenge.Purpose);
        Assert.Equal(FixedNow, challenge.CreatedAt);
        Assert.Equal(FixedNow.AddMinutes(3), challenge.ExpiresAt);
        Assert.Equal(FixedNow.AddMinutes(10), challenge.FlowExpiresAt);
        Assert.Equal((short)0, challenge.AttemptCount);
        Assert.Equal((short)5, challenge.MaxAttempts);
        Assert.Equal((short)0, challenge.ResendCount);
        Assert.Null(challenge.ConsumedAt);
        Assert.False(challenge.IsRevoked);
        Assert.Equal(32, challenge.OtpHash.Length);
        Assert.True(service.IsUsable(challenge, FixedNow));
    }

    [Fact]
    public void ChallengeAtExpirationBoundary_IsExpiredAndUnusable()
    {
        var service = CreateService();
        var challenge = CreateChallenge();

        Assert.False(service.IsExpired(challenge, challenge.ExpiresAt.AddTicks(-1)));
        Assert.True(service.IsExpired(challenge, challenge.ExpiresAt));
        Assert.False(service.IsUsable(challenge, challenge.ExpiresAt));
    }

    [Fact]
    public void ConsumedChallenge_IsNotUsable()
    {
        var service = CreateService();
        var challenge = CreateChallenge();
        challenge.ConsumedAt = FixedNow.AddMinutes(1);

        Assert.False(service.IsUsable(challenge, FixedNow.AddMinutes(1)));
    }

    [Fact]
    public void RevokedChallenge_IsNotUsable()
    {
        var service = CreateService();
        var challenge = CreateChallenge();

        service.RevokeChallenge(challenge);

        Assert.True(challenge.IsRevoked);
        Assert.False(service.IsUsable(challenge, FixedNow));
    }

    [Fact]
    public void FifthFailedAttempt_RevokesChallengeAndPreventsFurtherAttempts()
    {
        var service = CreateService();
        var challenge = CreateChallenge();
        challenge.AttemptCount = 4;

        var recorded = service.TryRecordFailedAttempt(challenge, FixedNow);
        var furtherAttempt = service.TryRecordFailedAttempt(challenge, FixedNow);

        Assert.True(recorded);
        Assert.Equal((short)5, challenge.AttemptCount);
        Assert.True(challenge.IsRevoked);
        Assert.False(service.CanAttempt(challenge));
        Assert.False(service.IsUsable(challenge, FixedNow));
        Assert.False(furtherAttempt);
        Assert.Equal((short)5, challenge.AttemptCount);
    }

    private static OtpService CreateService() =>
        new(Options.Create(new OtpOptions()), Enumerable.Range(0, 32).Select(index => (byte)index).ToArray());

    private static User CreateUser() => new()
    {
        Id = Guid.Parse("5f729b66-819c-468d-a4f4-1a96ed3b5b12"),
        Email = "student@example.com",
        NormalizedEmail = "STUDENT@EXAMPLE.COM",
        FullName = "Nguyen Van A",
        PasswordHash = "test-only-hash",
        CreatedAt = FixedNow
    };

    private static OtpChallenge CreateChallenge() => new()
    {
        Id = Guid.Parse("f424a13e-77c0-4b8c-832c-c3d6d27c3294"),
        UserId = Guid.Parse("5f729b66-819c-468d-a4f4-1a96ed3b5b12"),
        AuthenticationFlowId = Guid.Parse("91f89c54-6ebf-45f9-b1d2-3a41d24c8fc0"),
        Purpose = "LOGIN",
        CreatedAt = FixedNow,
        ExpiresAt = FixedNow.AddMinutes(3),
        FlowExpiresAt = FixedNow.AddMinutes(10),
        MaxAttempts = 5,
        OtpHash = new byte[32]
    };
}
