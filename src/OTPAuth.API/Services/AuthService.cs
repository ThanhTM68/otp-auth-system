using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OTPAuth.API.Data;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;

namespace OTPAuth.API.Services;

public interface IAuthService
{
    Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<SendOtpResult> SendOtpAsync(SendOtpRequest request, CancellationToken cancellationToken = default);
    Task<VerifyOtpResult> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default);
    Task<ResendOtpResult> ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken = default);
    Task<CurrentUserResponse?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public enum RegistrationStatus
{
    Success,
    DuplicateEmail,
    PersistenceFailure
}

public sealed record RegistrationResult(RegistrationStatus Status, RegisterResponse? Response = null);

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    PersistenceFailure
}

public sealed record LoginResult(LoginStatus Status, LoginResponse? Response = null);

public enum SendOtpStatus
{
    Success,
    NotAvailable,
    EmailDeliveryFailure,
    PersistenceFailure
}

public sealed record SendOtpResult(SendOtpStatus Status, SendOtpResponse? Response = null);

public enum VerifyOtpStatus
{
    Success,
    VerificationFailed,
    Expired,
    NotCurrent,
    MaxAttempts,
    NotSent,
    PersistenceFailure
}

public sealed record VerifyOtpResult(VerifyOtpStatus Status, VerifyOtpResponse? Response = null);

public enum ResendOtpStatus
{
    Success,
    NotAvailable,
    Cooldown,
    EmailDeliveryFailure,
    PersistenceFailure
}

public sealed record ResendOtpResult(
    ResendOtpStatus Status,
    ResendOtpResponse? Response = null,
    int? RetryAfterSeconds = null);

public class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider,
    IOtpService otpService,
    IEmailService emailService,
    IJwtTokenService jwtTokenService,
    IAuditService auditService) : IAuthService
{
    private readonly string dummyPasswordHash = passwordHasher.HashPassword(
        new User(),
        "not-a-user-password-and-not-a-secret");

    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email!;
        var normalizedEmail = EmailNormalizer.Normalize(email);

        if (await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return new RegistrationResult(RegistrationStatus.DuplicateEmail);
        }

        var createdAt = timeProvider.GetUtcNow();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            FullName = request.FullName!,
            IsActive = true,
            CreatedAt = createdAt
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        dbContext.Users.Add(user);
        auditService.Record(new AuditEvent(AuditEventTypes.RegisterSuccess, true, UserId: user.Id));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEmailViolation(exception))
        {
            return new RegistrationResult(RegistrationStatus.DuplicateEmail);
        }
        catch (DbUpdateException)
        {
            return new RegistrationResult(RegistrationStatus.PersistenceFailure);
        }

        return new RegistrationResult(
            RegistrationStatus.Success,
            new RegisterResponse(user.Id, user.Email, user.FullName, user.IsActive, user.CreatedAt));
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email!);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail,
            cancellationToken);

        var passwordVerification = passwordHasher.VerifyHashedPassword(
            user ?? new User(),
            user?.PasswordHash ?? dummyPasswordHash,
            request.Password!);

        if (user is null || !user.IsActive || passwordVerification == PasswordVerificationResult.Failed)
        {
            await auditService.TryRecordAsync(new AuditEvent(
                AuditEventTypes.LoginPasswordFailed,
                false,
                UserId: user?.Id,
                ReasonCode: AuditReasonCodes.InvalidCredentials), cancellationToken);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        var openChallenges = await dbContext.OtpChallenges
            .Where(challenge => challenge.UserId == user.Id &&
                challenge.Purpose == "LOGIN" &&
                !challenge.IsRevoked &&
                challenge.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        OtpChallenge challenge;
        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            foreach (var openChallenge in openChallenges)
            {
                otpService.RevokeChallenge(openChallenge);
            }

            if (openChallenges.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            challenge = otpService.CreatePendingLoginChallenge(user, timeProvider.GetUtcNow());
            dbContext.OtpChallenges.Add(challenge);
            auditService.Record(new AuditEvent(
                AuditEventTypes.LoginPasswordSuccess,
                true,
                UserId: user.Id));
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return new LoginResult(LoginStatus.PersistenceFailure);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return new LoginResult(LoginStatus.PersistenceFailure);
        }

        return new LoginResult(
            LoginStatus.Success,
            new LoginResponse(
                RequiresOtp: true,
                ChallengeId: challenge.Id,
                Purpose: challenge.Purpose,
                OtpSent: false,
                MaskedEmail: MaskEmail(user.Email),
                FlowExpiresAt: challenge.FlowExpiresAt));
    }

    public async Task<SendOtpResult> SendOtpAsync(
        SendOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        var challenge = await dbContext.OtpChallenges
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.ChallengeId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (challenge is null || challenge.User is null || !challenge.User.IsActive ||
            challenge.Purpose != "LOGIN" || challenge.IsRevoked || challenge.ConsumedAt is not null ||
            now >= challenge.FlowExpiresAt || !otpService.IsPending(challenge))
        {
            return new SendOtpResult(SendOtpStatus.NotAvailable);
        }

        OtpChallengeCreation creation;
        try
        {
            creation = otpService.PrepareFirstSend(challenge, now);
            auditService.Record(new AuditEvent(
                AuditEventTypes.OtpSendRequested,
                true,
                challenge.UserId,
                challenge.Id));
            auditService.Record(new AuditEvent(
                AuditEventTypes.OtpCreated,
                true,
                challenge.UserId,
                challenge.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return new SendOtpResult(SendOtpStatus.NotAvailable);
        }
        catch (DbUpdateException)
        {
            return new SendOtpResult(SendOtpStatus.PersistenceFailure);
        }

        try
        {
            await emailService.SendOtpAsync(
                new OtpEmailMessage(challenge.User.Email, creation.Otp, challenge.ExpiresAt!.Value),
                cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            var revoked = await RevokeAfterFailedDeliveryAsync(challenge.Id, cancellationToken);
            await auditService.TryRecordAsync(new AuditEvent(
                AuditEventTypes.OtpDeliveryFailed,
                false,
                challenge.UserId,
                challenge.Id,
                AuditReasonCodes.DeliveryFailed), cancellationToken);
            return new SendOtpResult(
                revoked ? SendOtpStatus.EmailDeliveryFailure : SendOtpStatus.PersistenceFailure);
        }

        var sentChallenge = await MarkChallengeSentAsync(challenge.Id, cancellationToken);
        if (sentChallenge is null)
        {
            var revoked = await RevokeAfterFailedDeliveryAsync(challenge.Id, cancellationToken);
            await auditService.TryRecordAsync(new AuditEvent(
                AuditEventTypes.OtpDeliveryFailed,
                false,
                challenge.UserId,
                challenge.Id,
                AuditReasonCodes.DeliveryFailed), cancellationToken);
            return new SendOtpResult(
                revoked ? SendOtpStatus.EmailDeliveryFailure : SendOtpStatus.PersistenceFailure);
        }

        return new SendOtpResult(
            SendOtpStatus.Success,
            new SendOtpResponse(
                sentChallenge.Id,
                sentChallenge.Purpose,
                OtpSent: true,
                MaskedEmail: MaskEmail(sentChallenge.User.Email),
                ExpiresAt: sentChallenge.ExpiresAt!.Value,
                FlowExpiresAt: sentChallenge.FlowExpiresAt,
                ResendAvailableAt: otpService.GetResendAvailableAt(sentChallenge)));
    }

    public async Task<VerifyOtpResult> VerifyOtpAsync(
        VerifyOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        const int maxConcurrencyRetries = 6;

        for (var attempt = 0; attempt < maxConcurrencyRetries; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            var challenge = await dbContext.OtpChallenges
                .Include(candidate => candidate.User)
                .SingleOrDefaultAsync(candidate => candidate.Id == request.ChallengeId, cancellationToken);

            if (challenge is null)
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpVerifyFailed,
                    false,
                    ReasonCode: AuditReasonCodes.ChallengeNotFound), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.VerificationFailed);
            }

            if (challenge.User is null || !challenge.User.IsActive)
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpVerifyFailed,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    AuditReasonCodes.UserInactive), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.VerificationFailed);
            }

            if (challenge.Purpose != "LOGIN")
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpVerifyFailed,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    AuditReasonCodes.WrongPurpose), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.VerificationFailed);
            }

            if (!otpService.HasBeenSent(challenge))
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpVerifyFailed,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    AuditReasonCodes.OtpNotSent), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.NotSent);
            }

            if (challenge.ConsumedAt is not null)
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpReplayRejected,
                    false,
                    challenge.UserId,
                    challenge.Id), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.NotCurrent);
            }

            if (otpService.IsExpired(challenge, now) || now >= challenge.FlowExpiresAt)
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpExpired,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    now >= challenge.FlowExpiresAt ? AuditReasonCodes.FlowExpired : AuditReasonCodes.OtpExpired), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.Expired);
            }

            if (!otpService.CanAttempt(challenge))
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpVerifyFailed,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    AuditReasonCodes.ChallengeLocked), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.MaxAttempts);
            }

            if (challenge.IsRevoked)
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpVerifyFailed,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    AuditReasonCodes.ChallengeRevoked), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.NotCurrent);
            }

            var otpMatches = otpService.VerifyOtp(challenge, request.Otp!);
            now = timeProvider.GetUtcNow();

            if (otpService.IsExpired(challenge, now) || now >= challenge.FlowExpiresAt)
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpExpired,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    now >= challenge.FlowExpiresAt ? AuditReasonCodes.FlowExpired : AuditReasonCodes.OtpExpired), cancellationToken);
                return new VerifyOtpResult(VerifyOtpStatus.Expired);
            }

            if (!otpMatches)
            {
                otpService.TryRecordFailedAttempt(challenge, now);
                auditService.Record(new AuditEvent(
                    AuditEventTypes.OtpVerifyFailed,
                    false,
                    challenge.UserId,
                    challenge.Id,
                    AuditReasonCodes.OtpMismatch));
                if (!otpService.CanAttempt(challenge))
                {
                    auditService.Record(new AuditEvent(
                        AuditEventTypes.OtpMaxAttemptsReached,
                        false,
                        challenge.UserId,
                        challenge.Id,
                        AuditReasonCodes.ChallengeLocked));
                }

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new VerifyOtpResult(
                        otpService.CanAttempt(challenge)
                            ? VerifyOtpStatus.VerificationFailed
                            : VerifyOtpStatus.MaxAttempts);
                }
                catch (DbUpdateConcurrencyException)
                {
                    dbContext.ChangeTracker.Clear();
                    continue;
                }
                catch (DbUpdateException)
                {
                    return new VerifyOtpResult(VerifyOtpStatus.PersistenceFailure);
                }
            }

            challenge.ConsumedAt = now;
            auditService.Record(new AuditEvent(
                AuditEventTypes.OtpVerifySuccess,
                true,
                challenge.UserId,
                challenge.Id));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateException)
            {
                return new VerifyOtpResult(VerifyOtpStatus.PersistenceFailure);
            }

            var token = jwtTokenService.CreateToken(challenge.User, now);
            await auditService.TryRecordAsync(new AuditEvent(
                AuditEventTypes.JwtIssued,
                true,
                challenge.UserId,
                challenge.Id), cancellationToken);
            return new VerifyOtpResult(
                VerifyOtpStatus.Success,
                new VerifyOtpResponse(token.AccessToken, token.TokenType, token.ExpiresIn, token.ExpiresAt));
        }

        return new VerifyOtpResult(VerifyOtpStatus.VerificationFailed);
    }

    public async Task<ResendOtpResult> ResendOtpAsync(
        ResendOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        const int maxConcurrencyRetries = 3;

        for (var attempt = 0; attempt < maxConcurrencyRetries; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            var previousChallenge = await dbContext.OtpChallenges
                .Include(challenge => challenge.User)
                .SingleOrDefaultAsync(challenge => challenge.Id == request.ChallengeId, cancellationToken);

            if (previousChallenge is null || previousChallenge.User is null || !previousChallenge.User.IsActive ||
                previousChallenge.Purpose != "LOGIN" || previousChallenge.IsRevoked ||
                previousChallenge.ConsumedAt is not null || !otpService.CanAttempt(previousChallenge) ||
                now >= previousChallenge.FlowExpiresAt || !otpService.HasBeenSent(previousChallenge))
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpResendFailed,
                    false,
                    previousChallenge?.UserId,
                    previousChallenge?.Id,
                    AuditReasonCodes.ResendNotAvailable), cancellationToken);
                return new ResendOtpResult(ResendOtpStatus.NotAvailable);
            }

            var resendAvailableAt = otpService.GetResendAvailableAt(previousChallenge);
            if (now < resendAvailableAt)
            {
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((resendAvailableAt - now).TotalSeconds));
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpResendFailed,
                    false,
                    previousChallenge.UserId,
                    previousChallenge.Id,
                    AuditReasonCodes.ResendCooldown), cancellationToken);
                return new ResendOtpResult(ResendOtpStatus.Cooldown, RetryAfterSeconds: retryAfterSeconds);
            }

            if (!otpService.CanResend(previousChallenge, now))
            {
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpResendFailed,
                    false,
                    previousChallenge.UserId,
                    previousChallenge.Id,
                    AuditReasonCodes.ResendNotAvailable), cancellationToken);
                return new ResendOtpResult(ResendOtpStatus.NotAvailable);
            }

            OtpChallengeCreation creation;
            try
            {
                await using var transaction = dbContext.Database.IsRelational()
                    ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                otpService.RevokeChallenge(previousChallenge);
                await dbContext.SaveChangesAsync(cancellationToken);

                creation = otpService.CreateResendLoginChallenge(previousChallenge.User, previousChallenge, now);
                dbContext.OtpChallenges.Add(creation.Challenge);
                auditService.Record(new AuditEvent(
                    AuditEventTypes.OtpCreated,
                    true,
                    creation.Challenge.UserId,
                    creation.Challenge.Id));
                await dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateException)
            {
                return new ResendOtpResult(ResendOtpStatus.PersistenceFailure);
            }

            try
            {
                await emailService.SendOtpAsync(
                    new OtpEmailMessage(
                        previousChallenge.User.Email,
                        creation.Otp,
                        creation.Challenge.ExpiresAt!.Value),
                    cancellationToken);
            }
            catch (EmailDeliveryException)
            {
                var revoked = await RevokeAfterFailedDeliveryAsync(creation.Challenge.Id, cancellationToken);
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpDeliveryFailed,
                    false,
                    creation.Challenge.UserId,
                    creation.Challenge.Id,
                    AuditReasonCodes.DeliveryFailed), cancellationToken);
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpResendFailed,
                    false,
                    creation.Challenge.UserId,
                    creation.Challenge.Id,
                    AuditReasonCodes.DeliveryFailed), cancellationToken);
                return new ResendOtpResult(
                    revoked ? ResendOtpStatus.EmailDeliveryFailure : ResendOtpStatus.PersistenceFailure);
            }

            var sentChallenge = await MarkChallengeSentAsync(creation.Challenge.Id, cancellationToken);
            if (sentChallenge is null)
            {
                var revoked = await RevokeAfterFailedDeliveryAsync(creation.Challenge.Id, cancellationToken);
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpDeliveryFailed,
                    false,
                    creation.Challenge.UserId,
                    creation.Challenge.Id,
                    AuditReasonCodes.DeliveryFailed), cancellationToken);
                await auditService.TryRecordAsync(new AuditEvent(
                    AuditEventTypes.OtpResendFailed,
                    false,
                    creation.Challenge.UserId,
                    creation.Challenge.Id,
                    AuditReasonCodes.DeliveryFailed), cancellationToken);
                return new ResendOtpResult(
                    revoked ? ResendOtpStatus.EmailDeliveryFailure : ResendOtpStatus.PersistenceFailure);
            }

            await auditService.TryRecordAsync(new AuditEvent(
                AuditEventTypes.OtpResendSuccess,
                true,
                sentChallenge.UserId,
                sentChallenge.Id), cancellationToken);

            return new ResendOtpResult(
                ResendOtpStatus.Success,
                new ResendOtpResponse(
                    sentChallenge.Id,
                    sentChallenge.Purpose,
                    sentChallenge.ExpiresAt!.Value,
                    sentChallenge.FlowExpiresAt,
                    otpService.GetResendAvailableAt(sentChallenge)));
        }

        return new ResendOtpResult(ResendOtpStatus.NotAvailable);
    }

    public async Task<CurrentUserResponse?> GetActiveUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => new CurrentUserResponse(user.Id, user.Email, user.FullName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<OtpChallenge?> MarkChallengeSentAsync(
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        const int maxConcurrencyRetries = 3;
        dbContext.ChangeTracker.Clear();

        for (var attempt = 0; attempt < maxConcurrencyRetries; attempt++)
        {
            var challenge = await dbContext.OtpChallenges
                .Include(candidate => candidate.User)
                .SingleOrDefaultAsync(candidate => candidate.Id == challengeId, cancellationToken);
            var sentAt = timeProvider.GetUtcNow();

            if (challenge is null || challenge.User is null || !challenge.User.IsActive ||
                challenge.Purpose != "LOGIN" || challenge.IsRevoked || challenge.ConsumedAt is not null ||
                !otpService.IsPrepared(challenge) || otpService.IsExpired(challenge, sentAt) ||
                sentAt >= challenge.FlowExpiresAt)
            {
                return null;
            }

            challenge.SentAt = sentAt;
            auditService.Record(new AuditEvent(
                AuditEventTypes.OtpSent,
                true,
                challenge.UserId,
                challenge.Id));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return challenge;
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                dbContext.ChangeTracker.Clear();
                return null;
            }
        }

        return null;
    }

    private async Task<bool> RevokeAfterFailedDeliveryAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        const int maxConcurrencyRetries = 3;

        for (var attempt = 0; attempt < maxConcurrencyRetries; attempt++)
        {
            var challenge = await dbContext.OtpChallenges
                .SingleOrDefaultAsync(candidate => candidate.Id == challengeId, cancellationToken);

            if (challenge is null || challenge.IsRevoked || challenge.ConsumedAt is not null)
            {
                return true;
            }

            otpService.RevokeChallenge(challenge);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsUniqueEmailViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "***";
        }

        var localPart = email[..atIndex];
        var visibleLength = Math.Min(2, localPart.Length);
        return $"{localPart[..visibleLength]}***{email[atIndex..]}";
    }
}
