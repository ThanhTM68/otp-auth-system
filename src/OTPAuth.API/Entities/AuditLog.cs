namespace OTPAuth.API.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? OtpChallengeId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ReasonCode { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
