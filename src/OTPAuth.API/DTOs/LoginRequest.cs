using System.ComponentModel.DataAnnotations;

namespace OTPAuth.API.DTOs;

public class LoginRequest
{
    private string? _email;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string? Email
    {
        get => _email;
        set => _email = value?.Trim();
    }

    [Required]
    [NotWhiteSpace]
    [StringLength(128, MinimumLength = 8)]
    public string? Password { get; set; }
}
