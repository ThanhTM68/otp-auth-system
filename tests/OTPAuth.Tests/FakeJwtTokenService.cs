using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

internal sealed class FakeJwtTokenService : IJwtTokenService
{
    public JwtTokenResult CreateToken(User user, DateTimeOffset issuedAt) =>
        new("test-access-token", "Bearer", 900, issuedAt.AddMinutes(15));
}
