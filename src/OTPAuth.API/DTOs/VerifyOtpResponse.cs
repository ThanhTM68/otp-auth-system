namespace OTPAuth.API.DTOs;

public sealed record VerifyOtpResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset ExpiresAt);
