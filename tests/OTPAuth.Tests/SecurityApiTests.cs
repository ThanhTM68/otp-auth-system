using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OTPAuth.API.Data;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

[Collection("Environment variable tests")]
public class SecurityApiTests
{
    [Fact]
    public async Task PasswordOnlyLogin_ShouldNotIssueJwt()
    {
        using var factory = new SecurityWebApplicationFactory();
        await AddActiveUserAsync(factory);
        using var client = CreateHttpsClient(factory);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password = "ValidPassword123!"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("requiresOtp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutJwt_ReturnsUnauthorized()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = CreateHttpsClient(factory);

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidJwt_ReturnsMinimalCurrentUser()
    {
        using var factory = new SecurityWebApplicationFactory();
        var user = await AddActiveUserAsync(factory);
        using var client = CreateHttpsClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(factory, user));

        using var response = await client.GetAsync("/api/auth/me");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(user.Email, body, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OtpHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessToken", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJub3QtYS11dWlkIn0.invalid")]
    public async Task InvalidJwt_ShouldReturnUnauthorized(string token)
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = CreateHttpsClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredJwt_ShouldReturnUnauthorized()
    {
        using var factory = new SecurityWebApplicationFactory();
        var user = await AddActiveUserAsync(factory);
        using var client = CreateHttpsClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateExpiredToken(user));

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidAuthRequest_ReturnsSanitizedValidationProblem()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = CreateHttpsClient(factory);

        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "not-an-email",
            password = "",
            fullName = ""
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VALIDATION_ERROR", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection string", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MailKit", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyOtpRequest_DoesNotAcceptClientSelectedUserId()
    {
        var requestProperties = typeof(VerifyOtpRequest).GetProperties().Select(property => property.Name);

        Assert.Equal(["ChallengeId", "Otp"], requestProperties.OrderBy(name => name));
    }

    private static HttpClient CreateHttpsClient(WebApplicationFactory<global::Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<User> AddActiveUserAsync(SecurityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "student@example.com",
            NormalizedEmail = "STUDENT@EXAMPLE.COM",
            FullName = "Nguyen Van A",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "ValidPassword123!");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static string CreateToken(SecurityWebApplicationFactory factory, User user)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .CreateToken(user, DateTimeOffset.UtcNow)
            .AccessToken;
    }

    private static string CreateExpiredToken(User user)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(SecurityWebApplicationFactory.JwtSigningKey),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            SecurityWebApplicationFactory.JwtIssuer,
            SecurityWebApplicationFactory.JwtAudience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())],
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
