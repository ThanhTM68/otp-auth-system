using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OTPAuth.API.Configuration;
using OTPAuth.API.Entities;

namespace OTPAuth.API.Services;

public sealed record OtpChallengeCreation(OtpChallenge Challenge, string Otp);

public interface IOtpService
{
    string GenerateOtp();
    byte[] HashOtp(OtpChallenge challenge, string otp);
    bool VerifyOtp(OtpChallenge challenge, string otp);
    OtpChallengeCreation CreateLoginChallenge(User user, DateTimeOffset createdAt);
    OtpChallengeCreation CreateResendLoginChallenge(User user, OtpChallenge previousChallenge, DateTimeOffset createdAt);
    bool IsExpired(OtpChallenge challenge, DateTimeOffset now);
    bool CanAttempt(OtpChallenge challenge);
    bool IsUsable(OtpChallenge challenge, DateTimeOffset now);
    bool TryRecordFailedAttempt(OtpChallenge challenge, DateTimeOffset now);
    DateTimeOffset GetResendAvailableAt(OtpChallenge challenge);
    bool CanResend(OtpChallenge challenge, DateTimeOffset now);
    void RevokeChallenge(OtpChallenge challenge);
}

public sealed class OtpService : IOtpService
{
    private readonly byte[] hashingKey;
    private readonly OtpOptions options;

    public OtpService(IOptions<OtpOptions> otpOptions, byte[] hashingKey)
    {
        options = otpOptions.Value;
        ValidateOptions(options);

        if (hashingKey.Length < 32)
        {
            throw new InvalidOperationException("Otp:HashingKey must contain at least 256 bits.");
        }

        this.hashingKey = hashingKey.ToArray();
    }

    public string GenerateOtp() => FormatOtp(
        RandomNumberGenerator.GetInt32(0, GetOtpUpperExclusive()),
        options.Length);

    public byte[] HashOtp(OtpChallenge challenge, string otp)
    {
        ValidateOtp(otp);
        return HMACSHA256.HashData(hashingKey, CreateCanonicalPayload(challenge, otp));
    }

    public bool VerifyOtp(OtpChallenge challenge, string otp)
    {
        ValidateOtp(otp);
        var expectedHash = HashOtp(challenge, otp);
        return challenge.OtpHash.Length == expectedHash.Length &&
            CryptographicOperations.FixedTimeEquals(challenge.OtpHash, expectedHash);
    }

    public OtpChallengeCreation CreateLoginChallenge(User user, DateTimeOffset createdAt)
    {
        var flowExpiresAt = createdAt.AddMinutes(options.FlowTtlMinutes);
        var expiresAt = Min(createdAt.AddMinutes(options.TtlMinutes), flowExpiresAt);
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthenticationFlowId = Guid.NewGuid(),
            Purpose = "LOGIN",
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            FlowExpiresAt = flowExpiresAt,
            ConsumedAt = null,
            AttemptCount = 0,
            MaxAttempts = options.MaxAttempts,
            ResendCount = 0,
            IsRevoked = false
        };

        var otp = GenerateOtp();
        challenge.OtpHash = HashOtp(challenge, otp);
        return new OtpChallengeCreation(challenge, otp);
    }

    public OtpChallengeCreation CreateResendLoginChallenge(
        User user,
        OtpChallenge previousChallenge,
        DateTimeOffset createdAt)
    {
        if (previousChallenge.Purpose != "LOGIN" || previousChallenge.ResendCount >= options.MaxResends)
        {
            throw new InvalidOperationException("The challenge cannot be resent.");
        }

        var expiresAt = Min(createdAt.AddMinutes(options.TtlMinutes), previousChallenge.FlowExpiresAt);
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthenticationFlowId = previousChallenge.AuthenticationFlowId,
            Purpose = "LOGIN",
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            FlowExpiresAt = previousChallenge.FlowExpiresAt,
            ConsumedAt = null,
            AttemptCount = 0,
            MaxAttempts = options.MaxAttempts,
            ResendCount = checked((short)(previousChallenge.ResendCount + 1)),
            IsRevoked = false
        };

        var otp = GenerateOtp();
        challenge.OtpHash = HashOtp(challenge, otp);
        return new OtpChallengeCreation(challenge, otp);
    }

    public bool IsExpired(OtpChallenge challenge, DateTimeOffset now) => now >= challenge.ExpiresAt;

    public bool CanAttempt(OtpChallenge challenge) => challenge.AttemptCount < challenge.MaxAttempts;

    public bool IsUsable(OtpChallenge challenge, DateTimeOffset now) =>
        challenge.Purpose == "LOGIN" &&
        !challenge.IsRevoked &&
        challenge.ConsumedAt is null &&
        CanAttempt(challenge) &&
        !IsExpired(challenge, now) &&
        now < challenge.FlowExpiresAt;

    public bool TryRecordFailedAttempt(OtpChallenge challenge, DateTimeOffset now)
    {
        if (!IsUsable(challenge, now))
        {
            return false;
        }

        challenge.AttemptCount++;
        if (!CanAttempt(challenge))
        {
            RevokeChallenge(challenge);
        }

        return true;
    }

    public DateTimeOffset GetResendAvailableAt(OtpChallenge challenge) =>
        challenge.CreatedAt.AddSeconds(options.ResendCooldownSeconds);

    public bool CanResend(OtpChallenge challenge, DateTimeOffset now) =>
        challenge.Purpose == "LOGIN" &&
        !challenge.IsRevoked &&
        challenge.ConsumedAt is null &&
        CanAttempt(challenge) &&
        challenge.ResendCount < options.MaxResends &&
        now < challenge.FlowExpiresAt &&
        now >= GetResendAvailableAt(challenge);

    public void RevokeChallenge(OtpChallenge challenge) => challenge.IsRevoked = true;

    public static string FormatOtp(int value) => FormatOtp(value, 6);

    private static string FormatOtp(int value, int length)
    {
        var upperExclusive = 1;
        for (var index = 0; index < length; index++)
        {
            upperExclusive *= 10;
        }

        if (value < 0 || value >= upperExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value.ToString($"D{length}", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private int GetOtpUpperExclusive()
    {
        var upperExclusive = 1;
        for (var index = 0; index < options.Length; index++)
        {
            upperExclusive *= 10;
        }

        return upperExclusive;
    }

    private void ValidateOtp(string otp)
    {
        if (otp.Length != options.Length || otp.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException($"OTP must contain exactly {options.Length} digits.", nameof(otp));
        }
    }

    private static byte[] CreateCanonicalPayload(OtpChallenge challenge, string otp)
    {
        var purposeBytes = Encoding.ASCII.GetBytes(challenge.Purpose);
        var otpBytes = Encoding.ASCII.GetBytes(otp);
        var payload = new byte[16 + 16 + 16 + 1 + purposeBytes.Length + 1 + otpBytes.Length];
        var offset = 0;

        challenge.AuthenticationFlowId.TryWriteBytes(payload.AsSpan(offset, 16));
        offset += 16;
        challenge.Id.TryWriteBytes(payload.AsSpan(offset, 16));
        offset += 16;
        challenge.UserId.TryWriteBytes(payload.AsSpan(offset, 16));
        offset += 16;
        payload[offset++] = checked((byte)purposeBytes.Length);
        purposeBytes.CopyTo(payload, offset);
        offset += purposeBytes.Length;
        payload[offset++] = checked((byte)otpBytes.Length);
        otpBytes.CopyTo(payload, offset);

        return payload;
    }

    private static void ValidateOptions(OtpOptions otpOptions)
    {
        if (otpOptions.Length != 6 || otpOptions.TtlMinutes != 3 ||
            otpOptions.FlowTtlMinutes != 10 || otpOptions.MaxAttempts is < 1 or > 5 ||
            otpOptions.ResendCooldownSeconds != 60 || otpOptions.MaxResends != 3)
        {
            throw new InvalidOperationException("OTP options must use 6 digits, 3-minute TTL, 10-minute flow TTL, 1-5 attempts, a 60-second resend cooldown, and 3 resends.");
        }
    }
}
