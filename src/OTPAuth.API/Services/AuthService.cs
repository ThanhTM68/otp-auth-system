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
}

public enum RegistrationStatus
{
    Success,
    DuplicateEmail,
    PersistenceFailure
}

public sealed record RegistrationResult(RegistrationStatus Status, RegisterResponse? Response = null);

public class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider) : IAuthService
{
    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email!;
        var normalizedEmail = email.ToUpperInvariant();

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

    private static bool IsUniqueEmailViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
