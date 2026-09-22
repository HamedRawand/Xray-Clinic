using NasimXray.Api.Data;
using NasimXray.Api.Models;

namespace NasimXray.Api.Services;

public class AuditService(AppDbContext db)
{
    public async Task LogAsync(int? userId, string action, string? entityName = null, string? entityId = null, string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
        });
        await db.SaveChangesAsync();
    }
}
