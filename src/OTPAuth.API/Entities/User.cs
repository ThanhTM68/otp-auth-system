namespace OTPAuth.API.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
