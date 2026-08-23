namespace OTPAuth.API.DTOs;

public sealed record LoginResponse(
    bool RequiresOtp,
    Guid ChallengeId,
    string Purpose,
    DateTimeOffset ExpiresAt,
    DateTimeOffset FlowExpiresAt,
    DateTimeOffset ResendAvailableAt);
