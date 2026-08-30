using Microsoft.EntityFrameworkCore;
using OTPAuth.API.Data;
using OTPAuth.API.Entities;

namespace OTPAuth.Tests;

public class DatabaseModelTests
{
    [Fact]
    public void UserEntity_DoesNotContainPlaintextPassword()
    {
        Assert.Null(typeof(User).GetProperty("Password"));
        Assert.NotNull(typeof(User).GetProperty(nameof(User.PasswordHash)));
    }

    [Fact]
    public void OtpChallengeEntity_StoresOnlyOtpHash()
    {
        Assert.Null(typeof(OtpChallenge).GetProperty("Otp"));
        Assert.NotNull(typeof(OtpChallenge).GetProperty(nameof(OtpChallenge.OtpHash)));
    }

    [Fact]
    public void PendingOtpChallenge_AllowsOnlyDeliveryMetadataToBeNull()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var entity = context.Model.FindEntityType(typeof(OtpChallenge));

        Assert.NotNull(entity);
        Assert.True(entity!.FindProperty(nameof(OtpChallenge.OtpHash))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(OtpChallenge.SentAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(OtpChallenge.ExpiresAt))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(OtpChallenge.FlowExpiresAt))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(OtpChallenge.MaxAttempts))!.IsNullable);
    }
}
