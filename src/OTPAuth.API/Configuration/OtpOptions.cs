namespace OTPAuth.API.Configuration;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    public int Length { get; init; } = 6;
    public int TtlMinutes { get; init; } = 3;
    public int FlowTtlMinutes { get; init; } = 10;
    public short MaxAttempts { get; init; } = 5;
}
