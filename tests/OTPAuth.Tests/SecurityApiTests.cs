using System.IdentityModel.Tokens.Jwt;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("UNAUTHORIZED", body, StringComparison.Ordinal);
        Assert.Contains("traceId", body, StringComparison.Ordinal);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        AssertProblemDetailsContentType(response);
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
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("UNAUTHORIZED", body, StringComparison.Ordinal);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
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
        Assert.Contains("traceId", body, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        AssertProblemDetailsContentType(response);
    }

    [Fact]
    public async Task NonDevelopmentHttpsResponse_UsesHstsOutsideLocalhost()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://security-test.example")
        });

        using var response = await client.GetAsync("/");

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task UnexpectedException_ReturnsGenericProblemWithoutSensitiveDiagnostics()
    {
        using var baseFactory = new SecurityWebApplicationFactory();
        using var logProvider = new CapturingLoggerProvider();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureLogging(logging => logging.AddProvider(logProvider));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthService>();
                services.AddScoped<IAuthService>(_ => new ThrowingAuthService(
                    new InvalidOperationException(ThrowingAuthService.SensitiveDiagnostic)));
            });
        });
        using var client = CreateHttpsClient(factory);

        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "student@example.com",
            password = "ValidPassword123!",
            fullName = "Nguyen Van A"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            Environments.Development,
            factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        Assert.Equal(
            "None",
            factory.Services.GetRequiredService<IConfiguration>()[
                "Logging:LogLevel:Microsoft.EntityFrameworkCore"]);
        Assert.Contains("INTERNAL_ERROR", body, StringComparison.Ordinal);
        Assert.Contains("traceId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ThrowingAuthService", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        AssertProblemDetailsContentType(response);

        var logOutput = logProvider.GetLogOutput();
        Assert.Contains("System.InvalidOperationException", logOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret", logOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("D:\\private", logOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(ThrowingAuthService.SensitiveDiagnostic, logOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestBodyLimitException_ReturnsSanitizedPayloadTooLargeProblem()
    {
        using var baseFactory = new SecurityWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthService>();
                services.AddScoped<IAuthService>(_ => new ThrowingAuthService(
                    new BadHttpRequestException(
                        "synthetic-body-parser-detail",
                        StatusCodes.Status413PayloadTooLarge)));
            }));
        using var client = CreateHttpsClient(factory);

        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "student@example.com",
            password = "ValidPassword123!",
            fullName = "Nguyen Van A"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("REQUEST_TOO_LARGE", body, StringComparison.Ordinal);
        Assert.Contains("traceId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-body-parser-detail", body, StringComparison.Ordinal);
        AssertProblemDetailsContentType(response);
    }

    [Fact]
    public async Task OversizedAuthenticationBody_ReturnsPayloadTooLarge()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = CreateHttpsClient(factory);
        using var content = JsonContent.Create(new
        {
            email = "student@example.com",
            password = new string('A', 17 * 1024),
            fullName = "Nguyen Van A"
        });

        using var response = await client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        AssertProblemDetailsContentType(response);
    }

    [Fact]
    public void JwtValidation_IsRestrictedToSignedHs256Tokens()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.Equal(
            [SecurityAlgorithms.HmacSha256],
            options.TokenValidationParameters.ValidAlgorithms);
    }

    [Fact]
    public void ApplicationStartup_RejectsReusedOtpAndJwtKeys()
    {
        var key = Convert.ToBase64String(Enumerable.Repeat((byte)77, 32).ToArray());
        var originalValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] =
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
            ["Otp__HashingKey"] = Environment.GetEnvironmentVariable("Otp__HashingKey"),
            ["Jwt__SigningKey"] = Environment.GetEnvironmentVariable("Jwt__SigningKey"),
            ["Jwt__Issuer"] = Environment.GetEnvironmentVariable("Jwt__Issuer"),
            ["Jwt__Audience"] = Environment.GetEnvironmentVariable("Jwt__Audience")
        };

        try
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__DefaultConnection",
                "Server=(local);Database=OtpAuthSecurityTests;Trusted_Connection=True;");
            Environment.SetEnvironmentVariable("Otp__HashingKey", key);
            Environment.SetEnvironmentVariable("Jwt__SigningKey", key);
            Environment.SetEnvironmentVariable("Jwt__Issuer", "OTPAuth.API.SecurityTests");
            Environment.SetEnvironmentVariable("Jwt__Audience", "OTPAuth.Client.SecurityTests");

            using var factory = new WebApplicationFactory<global::Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains(
                "Otp:HashingKey and Jwt:SigningKey must be different keys.",
                exception.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            foreach (var (name, value) in originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
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

    private static void AssertProblemDetailsContentType(HttpResponseMessage response) =>
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

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

    private sealed class ThrowingAuthService(Exception exception) : IAuthService
    {
        public const string SensitiveDiagnostic =
            "SqlException at D:\\private\\AuthService.cs; Password=synthetic-secret";

        public Task<RegistrationResult> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<LoginResult> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<VerifyOtpResult> VerifyOtpAsync(
            VerifyOtpRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<ResendOtpResult> ResendOtpAsync(
            ResendOtpRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<CurrentUserResponse?> GetActiveUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> entries = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public string GetLogOutput() => string.Join(Environment.NewLine, entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                entries.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    entries.Enqueue(exception.ToString());
                }
            }
        }
    }
}
