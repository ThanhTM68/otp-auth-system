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

public class RegistrationTests
{
    [Fact]
    public async Task ValidRegistration_PersistsHashedPassword()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var password = "ValidPassword123!";

        var result = await service.RegisterAsync(new RegisterRequest
        {
            Email = "student@example.com",
            Password = password,
            FullName = "Nguyen Van A"
        });

        var user = await context.Users.SingleAsync();
        Assert.Equal(RegistrationStatus.Success, result.Status);
        Assert.Equal("student@example.com", user.Email);
        Assert.NotEqual(password, user.PasswordHash);
        Assert.Single(context.AuditLogs, audit => audit.EventType == "REGISTER_SUCCESS" && audit.Success);
        Assert.Null(typeof(User).GetProperty("Password"));
    }

    [Fact]
    public async Task DuplicateNormalizedEmail_IsRejectedWithoutSecondUser()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        await service.RegisterAsync(new RegisterRequest
        {
            Email = "Test@Example.com",
            Password = "ValidPassword123!",
            FullName = "Nguyen Van A"
        });
        var duplicate = await service.RegisterAsync(new RegisterRequest
        {
            Email = "test@example.com",
            Password = "AnotherPassword123!",
            FullName = "Nguyen Van B"
        });

        Assert.Equal(RegistrationStatus.DuplicateEmail, duplicate.Status);
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Theory]
    [InlineData("not-an-email", "ValidPassword123!", "Nguyen Van A")]
    [InlineData("student@example.com", "", "Nguyen Van A")]
    public void InvalidRegistrationRequest_FailsDataAnnotationValidation(string email, string password, string fullName)
    {
        var request = new RegisterRequest { Email = email, Password = password, FullName = fullName };
        var validationResults = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true);

        Assert.False(valid);
    }

    [Fact]
    public void RegisterRequest_TrimsEmailAndFullName()
    {
        var request = new RegisterRequest
        {
            Email = "  student@example.com  ",
            Password = "ValidPassword123!",
            FullName = "  Nguyen Van A  "
        };

        Assert.Equal("student@example.com", request.Email);
        Assert.Equal("Nguyen Van A", request.FullName);
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

    private static OtpService CreateOtpService() =>
        new(Options.Create(new OtpOptions()), Enumerable.Range(0, 32).Select(index => (byte)index).ToArray());
}
