using System.ComponentModel.DataAnnotations;

namespace OTPAuth.API.DTOs;

public sealed class NotWhiteSpaceAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text);
}
