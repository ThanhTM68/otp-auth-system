namespace OTPAuth.API.DTOs;

public sealed record LoginResponse(
    bool RequiresOtp,
    Guid ChallengeId,
    string Purpose,
    bool OtpSent,
    string MaskedEmail,
    DateTimeOffset FlowExpiresAt);
