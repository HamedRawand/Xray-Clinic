using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Shared.Dtos.Audit;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = nameof(Permission.ViewAuditLog))]
public class AuditLogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AuditLogDto>>> GetAll([FromQuery] int? userId, [FromQuery] string? action)
    {
        var query = db.AuditLogs.AsNoTracking().Include(a => a.User).AsQueryable();

        if (userId.HasValue) query = query.Where(a => a.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);

        var list = await query.OrderByDescending(a => a.Timestamp).Take(500).ToListAsync();
        return list.Select(a => new AuditLogDto
        {
            Id = a.Id,
            UserId = a.UserId,
            UserName = a.User?.FullName,
            Action = a.Action,
            EntityName = a.EntityName,
            EntityId = a.EntityId,
            Details = a.Details,
            Timestamp = a.Timestamp,
        }).ToList();
    }
}
