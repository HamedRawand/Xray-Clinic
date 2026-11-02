using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos;
using NasimXray.Shared.Dtos.Audit;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = nameof(Permission.ViewAuditLog))]
public class AuditLogsController(AppDbContext db, ClinicClock clock) : ControllerBase
{
    // DateFrom/DateTo are inclusive clinic-local calendar dates (like the exams register).
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> GetAll(
        [FromQuery] int? userId, [FromQuery] string? action, [FromQuery] string? search,
        [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo,
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (userId.HasValue) query = query.Where(a => a.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.Action, pattern) ||
                (a.EntityName != null && EF.Functions.ILike(a.EntityName, pattern)) ||
                (a.EntityId != null && EF.Functions.ILike(a.EntityId, pattern)) ||
                (a.Details != null && EF.Functions.ILike(a.Details, pattern)) ||
                (a.User != null && EF.Functions.ILike(a.User.FullName, pattern)));
        }

        var from = dateFrom;
        var to = dateTo;
        if (from.HasValue && to.HasValue && from > to) (from, to) = (to, from);
        if (from.HasValue)
        {
            var startUtc = clock.StartOfDayUtc(from.Value);
            query = query.Where(a => a.Timestamp >= startUtc);
        }
        if (to.HasValue)
        {
            var endUtc = clock.StartOfDayUtc(to.Value.AddDays(1));
            query = query.Where(a => a.Timestamp < endUtc);
        }

        var total = await query.CountAsync();
        var (currentPage, size) = Paging.Normalize(page, pageSize, total);

        var list = await query
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Skip((currentPage - 1) * size).Take(size)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                UserId = a.UserId,
                UserName = a.User != null ? a.User.FullName : null,
                Action = a.Action,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                Details = a.Details,
                Timestamp = a.Timestamp,
            })
            .ToListAsync();

        return new PagedResult<AuditLogDto>
        {
            Items = list,
            TotalCount = total,
            Page = currentPage,
            PageSize = size,
        };
    }

    // The distinct actions that have been logged, for the filter drop-down.
    [HttpGet("actions")]
    public async Task<ActionResult<List<string>>> Actions() =>
        await db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
}
