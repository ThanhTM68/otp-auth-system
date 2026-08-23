using OTPAuth.API.Data;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

internal sealed class FakeAuditService(AppDbContext dbContext) : IAuditService
{
    public void Record(AuditEvent auditEvent)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = auditEvent.UserId,
            OtpChallengeId = auditEvent.OtpChallengeId,
            EventType = auditEvent.EventType,
            Success = auditEvent.Success,
            ReasonCode = auditEvent.ReasonCode,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task TryRecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Record(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
