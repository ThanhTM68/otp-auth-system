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
    Task<VerifyOtpResult> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default);
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
    EmailDeliveryFailure,
    PersistenceFailure
}

public sealed record LoginResult(LoginStatus Status, LoginResponse? Response = null);

public enum VerifyOtpStatus
{
    Success,
    VerificationFailed,
    PersistenceFailure
}

public sealed record VerifyOtpResult(VerifyOtpStatus Status, VerifyOtpResponse? Response = null);

public class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider,
    IOtpService otpService,
    IEmailService emailService,
    IJwtTokenService jwtTokenService) : IAuthService
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
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            EventType = "REGISTER_SUCCESS",
            Success = true,
            CreatedAt = createdAt
        });

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
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        var openChallenges = await dbContext.OtpChallenges
            .Where(challenge => challenge.UserId == user.Id &&
                challenge.Purpose == "LOGIN" &&
                !challenge.IsRevoked &&
                challenge.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var openChallenge in openChallenges)
        {
            otpService.RevokeChallenge(openChallenge);
        }

        var creation = otpService.CreateLoginChallenge(user, timeProvider.GetUtcNow());
        var challenge = creation.Challenge;
        dbContext.OtpChallenges.Add(challenge);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return new LoginResult(LoginStatus.PersistenceFailure);
        }

        try
        {
            await emailService.SendOtpAsync(
                new OtpEmailMessage(user.Email, creation.Otp, challenge.ExpiresAt),
                cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            otpService.RevokeChallenge(challenge);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return new LoginResult(LoginStatus.PersistenceFailure);
            }

            return new LoginResult(LoginStatus.EmailDeliveryFailure);
        }

        return new LoginResult(
            LoginStatus.Success,
            new LoginResponse(
                RequiresOtp: true,
                ChallengeId: challenge.Id,
                Purpose: challenge.Purpose,
                ExpiresAt: challenge.ExpiresAt,
                FlowExpiresAt: challenge.FlowExpiresAt,
                ResendAvailableAt: challenge.CreatedAt.AddMinutes(1)));
    }

    public async Task<VerifyOtpResult> VerifyOtpAsync(
        VerifyOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        const int maxConcurrencyRetries = 3;

        for (var attempt = 0; attempt < maxConcurrencyRetries; attempt++)
        {
            var challenge = await dbContext.OtpChallenges
                .Include(candidate => candidate.User)
                .SingleOrDefaultAsync(candidate => candidate.Id == request.ChallengeId, cancellationToken);

            if (challenge is null || challenge.User is null || !challenge.User.IsActive ||
                challenge.Purpose != "LOGIN" || !otpService.IsUsable(challenge, now))
            {
                return new VerifyOtpResult(VerifyOtpStatus.VerificationFailed);
            }

            if (!otpService.VerifyOtp(challenge, request.Otp!))
            {
                otpService.TryRecordFailedAttempt(challenge, now);

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new VerifyOtpResult(VerifyOtpStatus.VerificationFailed);
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
            return new VerifyOtpResult(
                VerifyOtpStatus.Success,
                new VerifyOtpResponse(token.AccessToken, token.TokenType, token.ExpiresIn, token.ExpiresAt));
        }

        return new VerifyOtpResult(VerifyOtpStatus.VerificationFailed);
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

    private static bool IsUniqueEmailViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
