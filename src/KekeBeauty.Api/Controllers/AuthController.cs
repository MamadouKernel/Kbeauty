using KekeBeauty.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

public sealed record RequestOtpRequest(string Telephone);
public sealed record VerifyOtpRequest(string Telephone, string Code);

[ApiController]
[Route("auth/otp")]
public sealed class AuthController : ControllerBase
{
    private readonly RequestOtpUseCase _requestOtpUseCase;
    private readonly VerifyOtpUseCase _verifyOtpUseCase;

    public AuthController(RequestOtpUseCase requestOtpUseCase, VerifyOtpUseCase verifyOtpUseCase)
    {
        _requestOtpUseCase = requestOtpUseCase;
        _verifyOtpUseCase = verifyOtpUseCase;
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestOtpRequest request, CancellationToken cancellationToken)
    {
        var result = await _requestOtpUseCase.ExecuteAsync(request.Telephone, cancellationToken);

        if (result.Success)
        {
            return Accepted(new { status = result.Status });
        }

        return result.Status == "invalid_phone"
            ? BadRequest(new { status = result.Status, message = result.Message })
            : StatusCode(StatusCodes.Status502BadGateway, new { status = result.Status, message = result.Message });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var result = await _verifyOtpUseCase.ExecuteAsync(request.Telephone, request.Code, cancellationToken);

        return result.Success
            ? Ok(new { status = result.Status, idUtilisateur = result.IdUtilisateur, isNewAccount = result.IsNewAccount })
            : BadRequest(new { status = result.Status });
    }
}
