using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Roles;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/custom-roles")]
[Authorize]
public class CustomRolesController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ManageUsers))]
    public async Task<ActionResult<List<CustomRoleDto>>> GetAll()
    {
        var roles = await db.CustomRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        return roles.Select(ToDto).ToList();
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageRoles))]
    public async Task<ActionResult<CustomRoleDto>> Create(CustomRoleUpsertRequest request)
    {
        if (await db.CustomRoles.AnyAsync(r => r.Name == request.Name))
            return BadRequest(new { message = "A role with that name already exists." });

        var role = new CustomRole { Name = request.Name.Trim(), Description = request.Description };
        role.SetPermissions(request.Permissions);
        db.CustomRoles.Add(role);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "CustomRoleCreated", nameof(CustomRole), role.Id.ToString(), role.Name);
        return CreatedAtAction(nameof(GetAll), ToDto(role));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = nameof(Permission.ManageRoles))]
    public async Task<ActionResult<CustomRoleDto>> Update(int id, CustomRoleUpsertRequest request)
    {
        var role = await db.CustomRoles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null) return NotFound();

        role.Name = request.Name.Trim();
        role.Description = request.Description;
        role.SetPermissions(request.Permissions);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "CustomRoleUpdated", nameof(CustomRole), role.Id.ToString(), role.Name);
        return ToDto(role);
    }

    private static CustomRoleDto ToDto(CustomRole r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        Permissions = r.GetPermissions().ToList(),
        CreatedAt = r.CreatedAt,
    };
}
