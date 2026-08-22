using Microsoft.AspNetCore.Mvc;
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
}
