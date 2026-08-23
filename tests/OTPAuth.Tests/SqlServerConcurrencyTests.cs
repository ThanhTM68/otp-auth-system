using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OTPAuth.API.Configuration;
using OTPAuth.API.Data;
using OTPAuth.API.DTOs;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

public sealed class SqlServerConcurrencyTests
{
    private const string RunSqlServerTestsVariable = "RUN_SQLSERVER_SECURITY_TESTS";
    private static readonly byte[] OtpHashingKey =
        Enumerable.Range(64, 32).Select(value => (byte)value).ToArray();

    [SqlServerFact]
    public async Task ConcurrentCorrectOtp_OnSqlServer_IssuesExactlyOneJwt()
    {
        var connectionString = GetOptInConnectionString();
        var now = DateTimeOffset.UtcNow;
        var otpService = CreateOtpService();
        var testData = await AddTestDataAsync(connectionString, otpService, now, "418205");

        try
        {
            using var barrier = new Barrier(2);
            var jwtService = new CountingJwtTokenService();
            await using var firstContext = CreateContext(
                connectionString,
                new FirstSaveBarrierInterceptor(barrier));
            await using var secondContext = CreateContext(
                connectionString,
                new FirstSaveBarrierInterceptor(barrier));
            var firstService = CreateAuthService(firstContext, otpService, jwtService, now);
            var secondService = CreateAuthService(secondContext, otpService, jwtService, now);
            var request = new VerifyOtpRequest { ChallengeId = testData.ChallengeId, Otp = "418205" };

            var results = await Task.WhenAll(
                firstService.VerifyOtpAsync(request),
                secondService.VerifyOtpAsync(request));

            Assert.Single(results, result => result.Status == VerifyOtpStatus.Success);
            Assert.Single(results, result => result.Status == VerifyOtpStatus.VerificationFailed);
            Assert.Equal(1, jwtService.CallCount);

            await using var verificationContext = CreateContext(connectionString);
            var persistedChallenge = await verificationContext.OtpChallenges
                .AsNoTracking()
                .SingleAsync(challenge => challenge.Id == testData.ChallengeId);
            Assert.NotNull(persistedChallenge.ConsumedAt);
        }
        finally
        {
            await RemoveTestDataAsync(connectionString, testData.UserId);
        }
    }

    [SqlServerFact]
    public async Task ConcurrentWrongOtp_OnSqlServer_StopsAtMaxAttempts()
    {
        var connectionString = GetOptInConnectionString();
        const int concurrentRequests = 10;
        var now = DateTimeOffset.UtcNow;
        var otpService = CreateOtpService();
        var testData = await AddTestDataAsync(connectionString, otpService, now, "418205");
        var contexts = new List<AppDbContext>();

        try
        {
            using var barrier = new Barrier(concurrentRequests);
            var jwtService = new CountingJwtTokenService();
            var services = Enumerable.Range(0, concurrentRequests)
                .Select(_ =>
                {
                    var context = CreateContext(
                        connectionString,
                        new FirstSaveBarrierInterceptor(barrier));
                    contexts.Add(context);
                    return CreateAuthService(context, otpService, jwtService, now);
                })
                .ToArray();
            var request = new VerifyOtpRequest { ChallengeId = testData.ChallengeId, Otp = "418206" };

            var results = await Task.WhenAll(
                services.Select(service => service.VerifyOtpAsync(request)));

            Assert.All(results, result => Assert.Equal(VerifyOtpStatus.VerificationFailed, result.Status));
            Assert.Equal(0, jwtService.CallCount);

            await using var verificationContext = CreateContext(connectionString);
            var persistedChallenge = await verificationContext.OtpChallenges
                .AsNoTracking()
                .SingleAsync(challenge => challenge.Id == testData.ChallengeId);
            Assert.Equal((short)5, persistedChallenge.AttemptCount);
            Assert.True(persistedChallenge.IsRevoked);
            Assert.Null(persistedChallenge.ConsumedAt);
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }

            await RemoveTestDataAsync(connectionString, testData.UserId);
        }
    }

    private static string GetOptInConnectionString()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RunSqlServerTestsVariable),
                "1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Set {RunSqlServerTestsVariable}=1 to run the SQL Server concurrency security tests.");
        }

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<global::Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        return configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required when SQL Server security tests are enabled.");
    }

    private static AppDbContext CreateContext(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString);
        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new AppDbContext(optionsBuilder.Options);
    }

    private static AuthService CreateAuthService(
        AppDbContext context,
        OtpService otpService,
        IJwtTokenService jwtTokenService,
        DateTimeOffset now) =>
        new(
            context,
            new PasswordHasher<User>(),
            new FixedTimeProvider(now),
            otpService,
            new FakeEmailService(),
            jwtTokenService,
            new FakeAuditService(context));

    private static OtpService CreateOtpService() =>
        new(Options.Create(new OtpOptions()), OtpHashingKey);

    private static async Task<(Guid UserId, Guid ChallengeId)> AddTestDataAsync(
        string connectionString,
        OtpService otpService,
        DateTimeOffset now,
        string otp)
    {
        await using var context = CreateContext(connectionString);
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"phase13-{Guid.NewGuid():N}@example.test",
            FullName = "Phase 13 SQL concurrency test",
            PasswordHash = "phase13-test-only-hash",
            IsActive = true,
            CreatedAt = now.AddMinutes(-1)
        };
        user.NormalizedEmail = EmailNormalizer.Normalize(user.Email);
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthenticationFlowId = Guid.NewGuid(),
            Purpose = "LOGIN",
            CreatedAt = now.AddMinutes(-1),
            ExpiresAt = now.AddMinutes(2),
            FlowExpiresAt = now.AddMinutes(9),
            AttemptCount = 0,
            MaxAttempts = 5,
            ResendCount = 0,
            IsRevoked = false
        };
        challenge.OtpHash = otpService.HashOtp(challenge, otp);
        context.Users.Add(user);
        context.OtpChallenges.Add(challenge);
        await context.SaveChangesAsync();
        return (user.Id, challenge.Id);
    }

    private static async Task RemoveTestDataAsync(string connectionString, Guid userId)
    {
        await using var context = CreateContext(connectionString);
        await context.AuditLogs.Where(log => log.UserId == userId).ExecuteDeleteAsync();
        await context.OtpChallenges.Where(challenge => challenge.UserId == userId).ExecuteDeleteAsync();
        await context.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingJwtTokenService : IJwtTokenService
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public JwtTokenResult CreateToken(User user, DateTimeOffset issuedAt)
        {
            var currentCall = Interlocked.Increment(ref callCount);
            return new JwtTokenResult(
                $"phase13-test-token-{currentCall}",
                "Bearer",
                900,
                issuedAt.AddMinutes(15));
        }
    }

    private sealed class FirstSaveBarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        private int hasSynchronized;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref hasSynchronized, 1) == 0)
            {
                var allParticipantsArrived = await Task.Run(
                    () => barrier.SignalAndWait(TimeSpan.FromSeconds(20)),
                    cancellationToken);
                if (!allParticipantsArrived)
                {
                    throw new TimeoutException("SQL concurrency test participants did not reach SaveChanges in time.");
                }
            }

            return result;
        }
    }

    public sealed class SqlServerFactAttribute : FactAttribute
    {
        public SqlServerFactAttribute()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(RunSqlServerTestsVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                Skip = $"Set {RunSqlServerTestsVariable}=1 to run against the configured SQL Server.";
            }
        }
    }
}
