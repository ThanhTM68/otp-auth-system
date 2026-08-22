namespace OTPAuth.API.Entities;

public class OtpChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AuthenticationFlowId { get; set; }
    public byte[] OtpHash { get; set; } = Array.Empty<byte>();
    public string Purpose { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset FlowExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public short AttemptCount { get; set; }
    public short MaxAttempts { get; set; } = 5;
    public short ResendCount { get; set; }
    public bool IsRevoked { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public User User { get; set; } = null!;
}
