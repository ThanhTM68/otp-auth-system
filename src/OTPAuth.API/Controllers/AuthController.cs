using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using OTPAuth.API.DTOs;
using OTPAuth.API.Services;

namespace OTPAuth.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegisterResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);

        return result.Status switch
        {
            RegistrationStatus.Success => StatusCode(StatusCodes.Status201Created, result.Response),
            RegistrationStatus.DuplicateEmail => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "Email đã được đăng ký.",
                "EMAIL_ALREADY_REGISTERED")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "Không thể hoàn tất đăng ký. Vui lòng thử lại sau.",
                    "INTERNAL_ERROR"))
        };
    }

    private static ProblemDetails CreateProblem(int status, string title, string code) =>
        new()
        {
            Status = status,
            Title = title,
            Extensions = { ["code"] = code }
        };

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);

        return result.Status switch
        {
            LoginStatus.Success => Ok(result.Response),
            LoginStatus.InvalidCredentials => Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Thông tin đăng nhập không hợp lệ.",
                "INVALID_CREDENTIALS")),
            LoginStatus.EmailDeliveryFailure => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                CreateProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    "Không thể gửi mã xác thực. Vui lòng thử lại sau.",
                    "OTP_DELIVERY_UNAVAILABLE")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "Không thể hoàn tất đăng nhập. Vui lòng thử lại sau.",
                    "INTERNAL_ERROR"))
        };
    }

    [HttpPost("verify-otp")]
    [ProducesResponseType(typeof(VerifyOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VerifyOtpResponse>> VerifyOtp(
        VerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyOtpAsync(request, cancellationToken);

        return result.Status switch
        {
            VerifyOtpStatus.Success => Ok(result.Response),
            VerifyOtpStatus.VerificationFailed => Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Mã xác thực không hợp lệ.",
                "OTP_VERIFICATION_FAILED")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "Không thể hoàn tất xác thực. Vui lòng thử lại sau.",
                    "INTERNAL_ERROR"))
        };
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var user = await authService.GetActiveUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "Tài khoản không còn hoạt động.",
                    "ACCOUNT_INACTIVE"));
        }

        return Ok(user);
    }
}
