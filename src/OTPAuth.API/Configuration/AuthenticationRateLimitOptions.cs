namespace OTPAuth.API.Configuration;

public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public AuthenticationRateLimitPolicyOptions Register { get; init; } = new();
    public AuthenticationRateLimitPolicyOptions Login { get; init; } = new();
    public AuthenticationRateLimitPolicyOptions SendOtp { get; init; } = new();
    public AuthenticationRateLimitPolicyOptions VerifyOtp { get; init; } = new();
    public AuthenticationRateLimitPolicyOptions ResendOtp { get; init; } = new();

    public bool IsValid() =>
        Register.IsValid() && Login.IsValid() && SendOtp.IsValid() &&
        VerifyOtp.IsValid() && ResendOtp.IsValid();
}

public sealed class AuthenticationRateLimitPolicyOptions
{
    public int PermitLimit { get; init; }
    public int WindowSeconds { get; init; }

    public bool IsValid() => PermitLimit > 0 && WindowSeconds > 0;
}

public static class AuthenticationRateLimitPolicies
{
    public const string Register = "register";
    public const string Login = "login";
    public const string SendOtp = "send-otp";
    public const string VerifyOtp = "verify-otp";
    public const string ResendOtp = "resend-otp";
}
