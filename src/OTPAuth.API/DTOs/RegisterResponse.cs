namespace OTPAuth.API.DTOs;

public sealed record RegisterResponse(Guid Id, string Email, string FullName, bool IsActive, DateTimeOffset CreatedAt);
