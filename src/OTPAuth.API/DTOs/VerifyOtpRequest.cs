using System.ComponentModel.DataAnnotations;

namespace OTPAuth.API.DTOs;

public sealed class VerifyOtpRequest
{
    [Required]
    public Guid? ChallengeId { get; init; }

    [Required]
    [RegularExpression("^[0-9]{6}$")]
    public string? Otp { get; init; }
}
