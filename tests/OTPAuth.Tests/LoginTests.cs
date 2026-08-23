using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OTPAuth.API.Data;
using OTPAuth.API.Configuration;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

public class LoginTests
{
    [Fact]
    public async Task ValidCredentials_CreateHashedLoginChallengeWithoutToken()
    {
        await using var context = CreateContext();
        await AddUserAsync(context, "student@example.com", "ValidPassword123!", isActive: true);
        var service = CreateService(context);

        var result = await service.LoginAsync(new LoginRequest
        {
            Email = "  STUDENT@example.com  ",
            Password = "ValidPassword123!"
        });

        var challenge = await context.OtpChallenges.SingleAsync();

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.True(result.Response!.RequiresOtp);
        Assert.Equal(challenge.Id, result.Response.ChallengeId);
        Assert.Equal("LOGIN", challenge.Purpose);
        Assert.Equal(32, challenge.OtpHash.Length);
        Assert.Equal(TimeSpan.FromMinutes(3), challenge.ExpiresAt - challenge.CreatedAt);
        Assert.Equal((short)0, challenge.AttemptCount);
        Assert.Equal((short)5, challenge.MaxAttempts);
        Assert.Null(challenge.ConsumedAt);
        Assert.False(challenge.IsRevoked);
        Assert.Null(typeof(LoginResponse).GetProperty("AccessToken"));
        Assert.Null(typeof(OtpChallenge).GetProperty("Otp"));
    }

    [Theory]
    [InlineData("student@example.com", "WrongPassword123!")]
    [InlineData("unknown@example.com", "ValidPassword123!")]
    public async Task UnknownOrIncorrectCredentials_AreRejectedWithoutChallenge(string email, string password)
    {
        await using var context = CreateContext();
        await AddUserAsync(context, "student@example.com", "ValidPassword123!", isActive: true);
        var service = CreateService(context);

        var result = await service.LoginAsync(new LoginRequest { Email = email, Password = password });

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.Response);
        Assert.Empty(context.OtpChallenges);
    }

    [Fact]
    public async Task InactiveUser_IsRejectedWithoutChallenge()
    {
        await using var context = CreateContext();
        await AddUserAsync(context, "inactive@example.com", "ValidPassword123!", isActive: false);
        var service = CreateService(context);

        var result = await service.LoginAsync(new LoginRequest
        {
            Email = "inactive@example.com",
            Password = "ValidPassword123!"
        });

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Empty(context.OtpChallenges);
    }

    [Fact]
    public async Task NewLogin_RevokesExistingOpenLoginChallenge()
    {
        await using var context = CreateContext();
        var user = await AddUserAsync(context, "student@example.com", "ValidPassword123!", isActive: true);
        context.OtpChallenges.Add(new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthenticationFlowId = Guid.NewGuid(),
            OtpHash = new byte[32],
            Purpose = "LOGIN",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
            FlowExpiresAt = DateTimeOffset.UtcNow.AddMinutes(9),
            MaxAttempts = 5
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context).LoginAsync(new LoginRequest
        {
            Email = "student@example.com",
            Password = "ValidPassword123!"
        });

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(2, await context.OtpChallenges.CountAsync());
        Assert.Single(context.OtpChallenges, challenge => challenge.IsRevoked);
        Assert.Single(context.OtpChallenges, challenge => !challenge.IsRevoked);
    }

    [Theory]
    [InlineData("not-an-email", "ValidPassword123!")]
    [InlineData("student@example.com", "       ")]
    public void InvalidLoginRequest_FailsDataAnnotationValidation(string email, string password)
    {
        var request = new LoginRequest { Email = email, Password = password };
        var validationResults = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true);

        Assert.False(valid);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AuthService CreateService(AppDbContext context) =>
        new(context, new PasswordHasher<User>(), TimeProvider.System, CreateOtpService(), new FakeEmailService(), new FakeJwtTokenService(), new FakeAuditService(context));

    private static async Task<User> AddUserAsync(AppDbContext context, string email, string password, bool isActive)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = EmailNormalizer.Normalize(email),
            FullName = "Nguyen Van A",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static OtpService CreateOtpService() =>
        new(Options.Create(new OtpOptions()), Enumerable.Range(0, 32).Select(index => (byte)index).ToArray());
}
