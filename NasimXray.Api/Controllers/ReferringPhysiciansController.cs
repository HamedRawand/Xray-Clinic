using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.ReferringPhysicians;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/referring-physicians")]
[Authorize]
public class ReferringPhysiciansController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ReferringPhysicianDto>>> GetAll([FromQuery] bool includeInactive = false)
    {
        var query = db.ReferringPhysicians.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(p => p.IsActive);
        var list = await query.OrderBy(p => p.Name).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageReferringPhysicians))]
    public async Task<ActionResult<ReferringPhysicianDto>> Create(ReferringPhysicianUpsertRequest request)
    {
        var entity = new ReferringPhysician
        {
            Name = request.Name,
            ClinicName = request.ClinicName,
            PhoneNumber = request.PhoneNumber,
            IsActive = request.IsActive,
        };
        db.ReferringPhysicians.Add(entity);
        await db.SaveChangesAsync();
        await audit.LogAsync(User.GetUserId(), "ReferringPhysicianCreated", nameof(ReferringPhysician), entity.Id.ToString(), entity.Name);
        return CreatedAtAction(nameof(GetAll), ToDto(entity));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = nameof(Permission.ManageReferringPhysicians))]
    public async Task<ActionResult<ReferringPhysicianDto>> Update(int id, ReferringPhysicianUpsertRequest request)
    {
        var entity = await db.ReferringPhysicians.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null) return NotFound();

        entity.Name = request.Name;
        entity.ClinicName = request.ClinicName;
        entity.PhoneNumber = request.PhoneNumber;
        entity.IsActive = request.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "ReferringPhysicianUpdated", nameof(ReferringPhysician), entity.Id.ToString(), entity.Name);
        return ToDto(entity);
    }

    private static ReferringPhysicianDto ToDto(ReferringPhysician p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ClinicName = p.ClinicName,
        PhoneNumber = p.PhoneNumber,
        IsActive = p.IsActive,
    };
}
