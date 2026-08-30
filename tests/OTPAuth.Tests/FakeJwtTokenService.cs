using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

internal sealed class FakeJwtTokenService : IJwtTokenService
{
    public int CallCount { get; private set; }

    public JwtTokenResult CreateToken(User user, DateTimeOffset issuedAt)
    {
        CallCount++;
        return new("test-access-token", "Bearer", 900, issuedAt.AddMinutes(15));
    }
}
