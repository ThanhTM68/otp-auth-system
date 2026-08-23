using System.ComponentModel.DataAnnotations;

namespace OTPAuth.API.DTOs;

public sealed class ResendOtpRequest
{
    [Required]
    public Guid? ChallengeId { get; init; }
}
