using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using OTPAuth.API.DTOs;
using OTPAuth.API.Configuration;
using OTPAuth.API.Services;

namespace OTPAuth.API.Controllers;

[ApiController]
[Route("api/auth")]
[RequestSizeLimit(16 * 1024)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.Register)]
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

    private ProblemDetails CreateProblem(int status, string title, string code) =>
        new()
        {
            Status = status,
            Title = title,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = HttpContext.TraceIdentifier
            }
        };

    [HttpPost("login")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.Login)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
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
                "Email hoặc mật khẩu không chính xác.",
                "INVALID_CREDENTIALS")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "Không thể hoàn tất đăng nhập. Vui lòng thử lại sau.",
                    "INTERNAL_ERROR"))
        };
    }

    [HttpPost("send-otp")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.SendOtp)]
    [ProducesResponseType(typeof(SendOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SendOtpResponse>> SendOtp(
        SendOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.SendOtpAsync(request, cancellationToken);

        return result.Status switch
        {
            SendOtpStatus.Success => Ok(result.Response),
            SendOtpStatus.NotAvailable => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Không thể gửi mã xác thực cho yêu cầu này.",
                "OTP_SEND_NOT_AVAILABLE")),
            SendOtpStatus.EmailDeliveryFailure => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                CreateProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    "Chưa thể gửi mã xác thực. Vui lòng thử lại sau.",
                    "OTP_DELIVERY_UNAVAILABLE")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "Không thể hoàn tất yêu cầu. Vui lòng thử lại sau.",
                    "INTERNAL_ERROR"))
        };
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.VerifyOtp)]
    [ProducesResponseType(typeof(VerifyOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VerifyOtpResponse>> VerifyOtp(
        VerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyOtpAsync(request, cancellationToken);

        return result.Status switch
        {
            VerifyOtpStatus.Success => Ok(result.Response),
            VerifyOtpStatus.VerificationFailed => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Mã xác thực không chính xác.",
                "OTP_VERIFICATION_FAILED")),
            VerifyOtpStatus.Expired => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Mã xác thực đã hết hạn. Vui lòng yêu cầu mã mới.",
                "OTP_EXPIRED")),
            VerifyOtpStatus.NotCurrent => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Mã này không còn hiệu lực. Vui lòng sử dụng mã mới nhất.",
                "OTP_NOT_CURRENT")),
            VerifyOtpStatus.MaxAttempts => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Bạn đã nhập sai quá số lần cho phép. Vui lòng yêu cầu mã mới.",
                "OTP_MAX_ATTEMPTS")),
            VerifyOtpStatus.NotSent => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Mã xác thực chưa được gửi.",
                "OTP_NOT_SENT")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "Không thể hoàn tất xác thực. Vui lòng thử lại sau.",
                    "INTERNAL_ERROR"))
        };
    }

    [HttpPost("resend-otp")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.ResendOtp)]
    [ProducesResponseType(typeof(ResendOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ResendOtpResponse>> ResendOtp(
        ResendOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.ResendOtpAsync(request, cancellationToken);

        return result.Status switch
        {
            ResendOtpStatus.Success => Ok(result.Response),
            ResendOtpStatus.NotAvailable => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Không thể gửi lại mã xác thực cho yêu cầu này.",
                "RESEND_NOT_AVAILABLE")),
            ResendOtpStatus.Cooldown => CreateCooldownResponse(result.RetryAfterSeconds!.Value),
            ResendOtpStatus.EmailDeliveryFailure => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                CreateProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    "Không thể gửi mã xác thực. Vui lòng thử lại sau.",
                    "OTP_DELIVERY_UNAVAILABLE")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "Không thể hoàn tất yêu cầu. Vui lòng thử lại sau.",
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

    private ObjectResult CreateCooldownResponse(int retryAfterSeconds)
    {
        Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var problem = CreateProblem(
            StatusCodes.Status429TooManyRequests,
            "Vui lòng chờ trước khi gửi lại mã xác thực.",
            "RESEND_COOLDOWN");
        problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;
        return StatusCode(StatusCodes.Status429TooManyRequests, problem);
    }
}
