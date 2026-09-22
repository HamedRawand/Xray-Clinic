using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Users;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = nameof(Permission.ManageUsers))]
public class UsersController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        var users = await db.Users.AsNoTracking().Include(u => u.CustomRole).OrderBy(u => u.Username).ToListAsync();
        return users.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(UserCreateRequest request)
    {
        if (await db.Users.AnyAsync(u => u.Username == request.Username))
            return BadRequest(new { message = "That username is already taken." });
        if (request.Password.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });

        var user = new User
        {
            Username = request.Username.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            FullName = request.FullName,
            Role = request.Role,
            CustomRoleId = request.CustomRoleId,
            MustChangePassword = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "UserCreated", nameof(User), user.Id.ToString(), user.Username);
        return CreatedAtAction(nameof(GetAll), await LoadDto(user.Id));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDto>> Update(int id, UserUpdateRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.FullName = request.FullName;
        user.Role = request.Role;
        user.CustomRoleId = request.CustomRoleId;
        user.IsActive = request.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "UserUpdated", nameof(User), user.Id.ToString(), user.Username);
        return await LoadDto(user.Id);
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, [FromServices] AuthService authService)
    {
        if (request.NewPassword.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });
        if (!await db.Users.AnyAsync(u => u.Id == id))
            return NotFound();

        await authService.AdminResetPasswordAsync(id, request.NewPassword);
        await audit.LogAsync(User.GetUserId(), "UserPasswordReset", nameof(User), id.ToString());
        return NoContent();
    }

    [HttpPost("{id}/unlock")]
    public async Task<ActionResult<UserDto>> Unlock(int id)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.IsLocked = false;
        user.FailedLoginAttempts = 0;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "UserUnlocked", nameof(User), user.Id.ToString(), user.Username);
        return await LoadDto(user.Id);
    }

    private async Task<UserDto> LoadDto(int id) =>
        ToDto((await db.Users.AsNoTracking().Include(u => u.CustomRole).FirstAsync(u => u.Id == id)));

    private static UserDto ToDto(User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        FullName = u.FullName,
        Role = u.Role,
        CustomRoleId = u.CustomRoleId,
        CustomRoleName = u.CustomRole?.Name,
        RoleDisplayName = u.RoleDisplayName,
        IsActive = u.IsActive,
        IsLocked = u.IsLocked,
        MustChangePassword = u.MustChangePassword,
        CreatedAt = u.CreatedAt,
    };
}
