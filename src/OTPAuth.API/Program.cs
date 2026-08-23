using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OTPAuth.API.Configuration;
using OTPAuth.API.Data;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;
using OTPAuth.API.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dữ liệu gửi lên không hợp lệ."
        };
        problem.Extensions["code"] = "VALIDATION_ERROR";
        return new BadRequestObjectResult(problem);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OTP Authentication API",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter a JWT access token."
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<OtpOptions>()
    .BindConfiguration(OtpOptions.SectionName)
    .Validate(options =>
        options.Length == 6 &&
        options.TtlMinutes == 3 &&
        options.FlowTtlMinutes == 10 &&
        options.MaxAttempts is >= 1 and <= 5 &&
        options.ResendCooldownSeconds == 60 &&
        options.MaxResends == 3,
        "OTP options must use 6 digits, 3-minute TTL, 10-minute flow TTL, 1-5 attempts, a 60-second resend cooldown, and 3 resends.")
    .ValidateOnStart();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

var encodedOtpHashingKey = builder.Configuration["Otp:HashingKey"]
    ?? throw new InvalidOperationException("Otp:HashingKey is required.");
var otpHashingKey = Convert.FromBase64String(encodedOtpHashingKey);
if (otpHashingKey.Length < 32)
{
    throw new InvalidOperationException("Otp:HashingKey must contain at least 256 bits.");
}

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");
if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
    string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
    jwtOptions.ExpirationMinutes <= 0)
{
    throw new InvalidOperationException("Jwt issuer, audience, and expiration are required.");
}

var encodedJwtSigningKey = jwtOptions.SigningKey;
if (string.IsNullOrWhiteSpace(encodedJwtSigningKey))
{
    throw new InvalidOperationException("Jwt:SigningKey is required.");
}

var jwtSigningKey = Convert.FromBase64String(encodedJwtSigningKey);
if (jwtSigningKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 256 bits.");
}

builder.Services.AddSingleton<IOtpService>(serviceProvider =>
    new OtpService(serviceProvider.GetRequiredService<IOptions<OtpOptions>>(), otpHashingKey));
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtTokenService>(new JwtTokenService(jwtOptions, jwtSigningKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtSigningKey),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
