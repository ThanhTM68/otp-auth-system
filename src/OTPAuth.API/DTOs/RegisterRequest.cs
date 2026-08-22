using System.ComponentModel.DataAnnotations;

namespace OTPAuth.API.DTOs;

public class RegisterRequest
{
    private string? _email;
    private string? _fullName;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string? Email
    {
        get => _email;
        set => _email = value?.Trim();
    }

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string? Password { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string? FullName
    {
        get => _fullName;
        set => _fullName = value?.Trim();
    }
}
