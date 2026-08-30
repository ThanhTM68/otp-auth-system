using Microsoft.EntityFrameworkCore;
using OTPAuth.API.Data;
using OTPAuth.API.Entities;

namespace OTPAuth.API.Services;

public static class AuditEventTypes
{
    public const string RegisterSuccess = "REGISTER_SUCCESS";
    public const string LoginPasswordSuccess = "LOGIN_PASSWORD_SUCCESS";
    public const string LoginPasswordFailed = "LOGIN_PASSWORD_FAILED";
    public const string OtpSendRequested = "OTP_SEND_REQUESTED";
    public const string OtpCreated = "OTP_CREATED";
    public const string OtpSent = "OTP_SENT";
    public const string OtpDeliveryFailed = "OTP_DELIVERY_FAILED";
    public const string OtpVerifyFailed = "OTP_VERIFY_FAILED";
    public const string OtpExpired = "OTP_EXPIRED";
    public const string OtpReplayRejected = "OTP_REPLAY_REJECTED";
    public const string OtpMaxAttemptsReached = "OTP_MAX_ATTEMPTS_REACHED";
    public const string OtpVerifySuccess = "OTP_VERIFY_SUCCESS";
    public const string OtpResendSuccess = "OTP_RESEND_SUCCESS";
    public const string OtpResendFailed = "OTP_RESEND_FAILED";
    public const string JwtIssued = "JWT_ISSUED";
}

public static class AuditReasonCodes
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string ChallengeNotFound = "CHALLENGE_NOT_FOUND";
    public const string UserInactive = "USER_INACTIVE";
    public const string WrongPurpose = "WRONG_PURPOSE";
    public const string ChallengeRevoked = "CHALLENGE_REVOKED";
    public const string ChallengeLocked = "CHALLENGE_LOCKED";
    public const string OtpExpired = "OTP_EXPIRED";
    public const string FlowExpired = "FLOW_EXPIRED";
    public const string OtpMismatch = "OTP_MISMATCH";
    public const string OtpNotSent = "OTP_NOT_SENT";
    public const string DeliveryFailed = "DELIVERY_FAILED";
    public const string ResendCooldown = "RESEND_COOLDOWN";
    public const string ResendLimitReached = "RESEND_LIMIT_REACHED";
    public const string ResendNotAvailable = "RESEND_NOT_AVAILABLE";
}

public sealed record AuditEvent(
    string EventType,
    bool Success,
    Guid? UserId = null,
    Guid? OtpChallengeId = null,
    string? ReasonCode = null);

public interface IAuditService
{
    void Record(AuditEvent auditEvent);
    Task TryRecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed class AuditService(
    AppDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<AuditService> logger) : IAuditService
{
    public void Record(AuditEvent auditEvent)
    {
        var httpContext = httpContextAccessor.HttpContext;
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = auditEvent.UserId,
            OtpChallengeId = auditEvent.OtpChallengeId,
            EventType = auditEvent.EventType,
            Success = auditEvent.Success,
            ReasonCode = auditEvent.ReasonCode,
            IpAddress = Truncate(httpContext?.Connection.RemoteIpAddress?.ToString(), 45),
            UserAgent = Truncate(httpContext?.Request.Headers.UserAgent.ToString(), 256),
            CorrelationId = Truncate(httpContext?.TraceIdentifier, 64),
            CreatedAt = timeProvider.GetUtcNow()
        });
    }

    public async Task TryRecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Record(auditEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogError("Security audit persistence failed for event {EventType}", auditEvent.EventType);
        }
    }

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, maximumLength)];
}
