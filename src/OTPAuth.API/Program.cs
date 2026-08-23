using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OTPAuth.API.Configuration;
using OTPAuth.API.Data;
using OTPAuth.API.Entities;
using OTPAuth.API.Services;

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
        options.MaxAttempts is >= 1 and <= 5,
        "OTP options must use 6 digits, 3-minute TTL, 10-minute flow TTL, and 1-5 attempts.")
    .ValidateOnStart();

var encodedOtpHashingKey = builder.Configuration["Otp:HashingKey"]
    ?? throw new InvalidOperationException("Otp:HashingKey is required.");
var otpHashingKey = Convert.FromBase64String(encodedOtpHashingKey);
if (otpHashingKey.Length < 32)
{
    throw new InvalidOperationException("Otp:HashingKey must contain at least 256 bits.");
}

builder.Services.AddSingleton<IOtpService>(serviceProvider =>
    new OtpService(serviceProvider.GetRequiredService<IOptions<OtpOptions>>(), otpHashingKey));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
