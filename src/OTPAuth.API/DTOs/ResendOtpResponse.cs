namespace OTPAuth.API.DTOs;

public sealed record ResendOtpResponse(
    Guid ChallengeId,
    string Purpose,
    DateTimeOffset ExpiresAt,
    DateTimeOffset FlowExpiresAt,
    DateTimeOffset ResendAvailableAt);
