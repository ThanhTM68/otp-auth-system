namespace OTPAuth.API.DTOs;

public sealed record CurrentUserResponse(Guid Id, string Email, string FullName);
