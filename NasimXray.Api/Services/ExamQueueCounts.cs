using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Services;

// Sizes of the work queues (all time) — used by the Exams screen buttons and the dashboard.
public static class ExamQueueCounts
{
    public static async Task<ExamQueueCountsDto> GetAsync(AppDbContext db)
    {
        var byStatus = await db.ExamOrders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        int CountOf(ExamOrderStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        return new ExamQueueCountsDto
        {
            Pending = CountOf(ExamOrderStatus.Pending),
            InProgress = CountOf(ExamOrderStatus.InProgress),
            Completed = CountOf(ExamOrderStatus.Completed),
            NotInvoiced = await db.ExamOrders.CountAsync(o => o.InvoiceId == null && o.Status != ExamOrderStatus.Cancelled),
            Unpaid = await db.ExamOrders.CountAsync(o => o.Invoice != null && o.Invoice.Status != InvoiceStatus.Paid),
        };
    }
}
