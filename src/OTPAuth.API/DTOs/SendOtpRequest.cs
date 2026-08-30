using System.ComponentModel.DataAnnotations;

namespace OTPAuth.API.DTOs;

public sealed class SendOtpRequest
{
    [Required]
    public Guid? ChallengeId { get; init; }
}
