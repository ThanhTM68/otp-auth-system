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
}
