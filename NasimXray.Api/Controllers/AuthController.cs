using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Auth;
using NasimXray.Shared.Dtos.Users;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        try
        {
            var result = await authService.LoginAsync(request.Username, request.Password);
            return result is null ? Unauthorized(new { message = "Invalid username or password." }) : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshRequest request)
    {
        var result = await authService.RefreshAsync(request.RefreshToken);
        return result is null ? Unauthorized(new { message = "Invalid or expired refresh token." }) : Ok(result);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var ok = await authService.ChangePasswordAsync(User.GetUserId(), request.CurrentPassword, request.NewPassword);
        return ok ? NoContent() : BadRequest(new { message = "Current password is incorrect." });
    }

    [HttpPost("verify-password")]
    [Authorize]
    public async Task<IActionResult> VerifyPassword(VerifyPasswordRequest request)
    {
        var ok = await authService.VerifyPasswordAsync(User.GetUserId(), request.Password);
        return ok ? NoContent() : Unauthorized();
    }
}
