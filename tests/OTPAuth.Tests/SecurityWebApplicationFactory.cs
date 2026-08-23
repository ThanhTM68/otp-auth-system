using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OTPAuth.API.Data;
using OTPAuth.API.Services;

namespace OTPAuth.Tests;

internal sealed class SecurityWebApplicationFactory : WebApplicationFactory<global::Program>
{
    public const string JwtIssuer = "OTPAuth.API.SecurityTests";
    public const string JwtAudience = "OTPAuth.Client.SecurityTests";
    public static readonly byte[] JwtSigningKey = Enumerable.Range(1, 32).Select(index => (byte)index).ToArray();

    private readonly string databaseName = Guid.NewGuid().ToString();

    public SecurityWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",
            "Server=(local);Database=OtpAuthSecurityTests;Trusted_Connection=True;");
        Environment.SetEnvironmentVariable("Otp__HashingKey",
            Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()));
        Environment.SetEnvironmentVariable("Jwt__SigningKey", Convert.ToBase64String(JwtSigningKey));
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<IEmailService>();
            services.AddScoped<IEmailService, FakeEmailService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("Otp__HashingKey", null);
            Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
            Environment.SetEnvironmentVariable("Jwt__Issuer", null);
            Environment.SetEnvironmentVariable("Jwt__Audience", null);
        }

        base.Dispose(disposing);
    }
}
