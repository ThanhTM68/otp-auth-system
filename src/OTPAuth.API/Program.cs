using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
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
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
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

var rateLimitOptions = builder.Configuration.GetSection(AuthenticationRateLimitOptions.SectionName)
    .Get<AuthenticationRateLimitOptions>()
    ?? throw new InvalidOperationException("RateLimiting configuration is required.");
if (!rateLimitOptions.IsValid())
{
    throw new InvalidOperationException("Rate limiting permit limits and windows must be positive.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddHttpContextAccessor();
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
    jwtOptions.ExpirationMinutes != 15)
{
    throw new InvalidOperationException("Jwt issuer and audience are required, and expiration must be 15 minutes.");
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
if (CryptographicOperations.FixedTimeEquals(otpHashingKey, jwtSigningKey))
{
    throw new InvalidOperationException("Otp:HashingKey and Jwt:SigningKey must be different keys.");
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
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers["WWW-Authenticate"] = "Bearer";
                await WriteProblemDetailsAsync(context.Response, new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Authentication is required.",
                    Extensions =
                    {
                        ["code"] = "UNAUTHORIZED",
                        ["traceId"] = context.HttpContext.TraceIdentifier
                    }
                }, context.HttpContext.RequestAborted);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.Headers["Cache-Control"] = "no-store";
        await WriteProblemDetailsAsync(context.HttpContext.Response, new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests. Please try again later.",
            Extensions =
            {
                ["code"] = "RATE_LIMITED",
                ["traceId"] = context.HttpContext.TraceIdentifier
            }
        }, cancellationToken);
    };
    options.AddPolicy(AuthenticationRateLimitPolicies.Register, context =>
        CreateFixedWindowPartition(context, rateLimitOptions.Register));
    options.AddPolicy(AuthenticationRateLimitPolicies.Login, context =>
        CreateFixedWindowPartition(context, rateLimitOptions.Login));
    options.AddPolicy(AuthenticationRateLimitPolicies.VerifyOtp, context =>
        CreateFixedWindowPartition(context, rateLimitOptions.VerifyOtp));
    options.AddPolicy(AuthenticationRateLimitPolicies.ResendOtp, context =>
        CreateFixedWindowPartition(context, rateLimitOptions.ResendOtp));
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        context.Abort();
    }
    catch (Exception exception)
    {
        var isRequestTooLarge = exception is BadHttpRequestException badRequestException &&
            badRequestException.StatusCode == StatusCodes.Status413PayloadTooLarge;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");
        if (isRequestTooLarge)
        {
            logger.LogWarning(
                "Request body rejected because it exceeded the configured limit. TraceId: {TraceId}",
                context.TraceIdentifier);
        }
        else
        {
            logger.LogError(
                "Unhandled exception of type {ExceptionType}. TraceId: {TraceId}",
                exception.GetType().FullName,
                context.TraceIdentifier);
        }

        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = isRequestTooLarge
            ? StatusCodes.Status413PayloadTooLarge
            : StatusCodes.Status500InternalServerError;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        await WriteProblemDetailsAsync(context.Response, new ProblemDetails
        {
            Status = context.Response.StatusCode,
            Title = isRequestTooLarge
                ? "The request body is too large."
                : "An unexpected error occurred.",
            Extensions =
            {
                ["code"] = isRequestTooLarge ? "REQUEST_TOO_LARGE" : "INTERNAL_ERROR",
                ["traceId"] = context.TraceIdentifier
            }
        }, context.RequestAborted);
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    if (context.Request.Path.StartsWithSegments("/api/auth"))
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";
    }

    if (context.Request.Path == "/" || context.Request.Path == "/index.html")
    {
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'";
    }

    await next();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.Use(async (context, next) =>
{
    const long maxAuthenticationRequestBodySize = 16 * 1024;
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/api/auth") &&
        context.Request.ContentLength is > maxAuthenticationRequestBodySize)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await WriteProblemDetailsAsync(context.Response, new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "The request body is too large.",
            Extensions =
            {
                ["code"] = "REQUEST_TOO_LARGE",
                ["traceId"] = context.TraceIdentifier
            }
        }, context.RequestAborted);
        return;
    }

    await next();
});
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static RateLimitPartition<string> CreateFixedWindowPartition(
    HttpContext context,
    AuthenticationRateLimitPolicyOptions policy) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });

static Task WriteProblemDetailsAsync(
    HttpResponse response,
    ProblemDetails problemDetails,
    CancellationToken cancellationToken) =>
    response.WriteAsJsonAsync(
        problemDetails,
        options: (JsonSerializerOptions?)null,
        contentType: "application/problem+json",
        cancellationToken: cancellationToken);

public partial class Program
{
}
