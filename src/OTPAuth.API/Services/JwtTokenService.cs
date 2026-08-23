using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using OTPAuth.API.Configuration;
using OTPAuth.API.Entities;

namespace OTPAuth.API.Services;

public sealed record JwtTokenResult(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    JwtTokenResult CreateToken(User user, DateTimeOffset issuedAt);
}

public sealed class JwtTokenService(JwtOptions options, byte[] signingKey) : IJwtTokenService
{
    private readonly SigningCredentials signingCredentials = new(
        new SymmetricSecurityKey(signingKey),
        SecurityAlgorithms.HmacSha256);

    public JwtTokenResult CreateToken(User user, DateTimeOffset issuedAt)
    {
        var expiresAt = issuedAt.AddMinutes(options.ExpirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: signingCredentials);

        return new JwtTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            (int)TimeSpan.FromMinutes(options.ExpirationMinutes).TotalSeconds,
            expiresAt);
    }
}
