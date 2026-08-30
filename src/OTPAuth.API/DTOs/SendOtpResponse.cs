namespace OTPAuth.API.DTOs;

public sealed record SendOtpResponse(
    Guid ChallengeId,
    string Purpose,
    bool OtpSent,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset FlowExpiresAt,
    DateTimeOffset ResendAvailableAt);
