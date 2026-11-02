using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.Stats;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

// Read-only reporting. Days are clinic-local (see ClinicClock); money figures are only filled in for
// callers with ViewBilling. Aggregation happens in memory over the period's rows (tens of thousands
// at most) because grouping by clinic-local day can't be expressed in SQL.
[ApiController]
[Route("api/stats")]
[Authorize(Policy = nameof(Permission.ViewExamOrders))]
public class StatsController(AppDbContext db, ClinicClock clock) : ControllerBase
{
    private const int MaxRangeDays = 1100;

    private bool CanSeeMoney => User.HasClaim("permission", nameof(Permission.ViewBilling));

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard()
    {
        var today = clock.Today;
        var startUtc = clock.StartOfDayUtc(today);
        var endUtc = clock.StartOfDayUtc(today.AddDays(1));

        var exams = await db.ExamOrders.AsNoTracking()
            .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
            .Select(o => new { o.PatientId, o.ExamTypeId, TypeName = o.ExamType.Name, o.ExamType.Modality, o.Status, o.Price, o.DiscountAmount })
            .ToListAsync();
        var active = exams.Where(e => e.Status != ExamOrderStatus.Cancelled).ToList();

        var dto = new DashboardDto
        {
            Date = today.ToDateTime(TimeOnly.MinValue),
            Exams = active.Count,
            Patients = active.Select(e => e.PatientId).Distinct().Count(),
            Cancelled = exams.Count - active.Count,
            NewPatients = await db.Patients.CountAsync(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtc),
            ByExamType = active.GroupBy(e => new { e.ExamTypeId, e.TypeName, e.Modality })
                .Select(g => new ExamTypeCountDto
                {
                    ExamTypeId = g.Key.ExamTypeId,
                    ExamTypeName = g.Key.TypeName,
                    Modality = g.Key.Modality,
                    ExamCount = g.Count(),
                    PatientCount = g.Select(e => e.PatientId).Distinct().Count(),
                })
                .OrderByDescending(c => c.ExamCount).ThenBy(c => c.ExamTypeName).ToList(),
            ByStatus = Enum.GetValues<ExamOrderStatus>()
                .Select(s => new StatusCountDto { Status = s, Count = exams.Count(e => e.Status == s) }).ToList(),
            Queues = await ExamQueueCounts.GetAsync(db),
        };

        if (CanSeeMoney)
        {
            var payments = await db.Payments.AsNoTracking()
                .Where(p => p.Timestamp >= startUtc && p.Timestamp < endUtc)
                .Select(p => new { p.Amount, p.Method }).ToListAsync();

            dto.Money = new MoneyTodayDto
            {
                Collected = payments.Sum(p => p.Amount),
                Cash = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
                Card = payments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount),
                Other = payments.Where(p => p.Method == PaymentMethod.Other).Sum(p => p.Amount),
                Discounts = active.Sum(e => e.DiscountAmount),
                FreeExams = active.Count(e => e.DiscountAmount > 0 && e.Price - e.DiscountAmount == 0),
                Invoiced = await db.Invoices.Where(i => i.CreatedAt >= startUtc && i.CreatedAt < endUtc)
                    .SumAsync(i => (decimal?)i.Total) ?? 0,
                Outstanding = await db.Invoices.Where(i => i.Status != InvoiceStatus.Paid)
                    .SumAsync(i => (decimal?)(i.Total - i.Payments.Sum(p => p.Amount))) ?? 0,
            };
        }

        return dto;
    }

    [HttpGet("report")]
    public async Task<ActionResult<StatsReportDto>> Report([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo)
    {
        var to = dateTo ?? clock.Today;
        var from = dateFrom ?? to;
        if (from > to) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > MaxRangeDays)
            return BadRequest(new { message = $"Choose a period of at most {MaxRangeDays} days." });

        var startUtc = clock.StartOfDayUtc(from);
        var endUtc = clock.StartOfDayUtc(to.AddDays(1));
        var money = CanSeeMoney;

        var exams = await db.ExamOrders.AsNoTracking()
            .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
            .Select(o => new
            {
                o.CreatedAt, o.PatientId, o.ExamTypeId, TypeName = o.ExamType.Name, o.ExamType.Modality,
                o.Price, o.DiscountAmount, o.Status, o.InvoiceId,
                DiscountedByName = o.DiscountedBy != null ? o.DiscountedBy.FullName : null,
                DoctorId = o.ReferringPhysicianId,
                DoctorName = o.ReferringPhysician != null ? o.ReferringPhysician.Name : null,
            })
            .ToListAsync();
        var active = exams.Where(e => e.Status != ExamOrderStatus.Cancelled).ToList();

        decimal? Revenue(IEnumerable<decimal> prices) => money ? prices.Sum() : null;
        decimal? Waived(IEnumerable<decimal> amounts) => money ? amounts.Sum() : null;

        var report = new StatsReportDto
        {
            From = from.ToDateTime(TimeOnly.MinValue),
            To = to.ToDateTime(TimeOnly.MinValue),
            TotalExams = active.Count,
            DistinctPatients = active.Select(e => e.PatientId).Distinct().Count(),
            CancelledExams = exams.Count - active.Count,
            NewPatients = await db.Patients.CountAsync(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtc),

            ByExamType = active.GroupBy(e => new { e.ExamTypeId, e.TypeName })
                .Select(g => new StatsRowDto
                {
                    Id = g.Key.ExamTypeId,
                    Label = g.Key.TypeName,
                    Exams = g.Count(),
                    Patients = g.Select(e => e.PatientId).Distinct().Count(),
                    Revenue = Revenue(g.Where(e => e.InvoiceId != null).Select(e => e.Price - e.DiscountAmount)),
                    Discount = Waived(g.Select(e => e.DiscountAmount)),
                })
                .OrderByDescending(r => r.Exams).ThenBy(r => r.Label).ToList(),

            ByModality = active.GroupBy(e => e.Modality ?? string.Empty)
                .Select(g => new StatsRowDto
                {
                    Label = string.IsNullOrEmpty(g.Key) ? null : g.Key,
                    Exams = g.Count(),
                    Patients = g.Select(e => e.PatientId).Distinct().Count(),
                    Revenue = Revenue(g.Where(e => e.InvoiceId != null).Select(e => e.Price - e.DiscountAmount)),
                    Discount = Waived(g.Select(e => e.DiscountAmount)),
                })
                .OrderByDescending(r => r.Exams).ThenBy(r => r.Label).ToList(),

            ByReferringPhysician = active.GroupBy(e => new { e.DoctorId, e.DoctorName })
                .Select(g => new StatsRowDto
                {
                    Id = g.Key.DoctorId,
                    Label = g.Key.DoctorName,
                    Exams = g.Count(),
                    Patients = g.Select(e => e.PatientId).Distinct().Count(),
                    Revenue = Revenue(g.Where(e => e.InvoiceId != null).Select(e => e.Price - e.DiscountAmount)),
                    Discount = Waived(g.Select(e => e.DiscountAmount)),
                })
                .OrderByDescending(r => r.Exams).ThenBy(r => r.Label).ToList(),
        };

        // Per-day rows combine exams (by clinic-local day) with, for billing users, payments received that day.
        var examsByDay = active.GroupBy(e => clock.LocalDate(e.CreatedAt))
            .ToDictionary(g => g.Key, g => (Exams: g.Count(), Patients: g.Select(e => e.PatientId).Distinct().Count()));

        var paymentRows = money
            ? await db.Payments.AsNoTracking()
                .Where(p => p.Timestamp >= startUtc && p.Timestamp < endUtc)
                .Select(p => new { p.Timestamp, p.Amount, p.Method, UserName = p.ReceivedBy.FullName })
                .ToListAsync()
            : null;
        var collectedByDay = paymentRows?.GroupBy(p => clock.LocalDate(p.Timestamp))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var days = examsByDay.Keys.Union(collectedByDay?.Keys ?? Enumerable.Empty<DateOnly>()).OrderBy(d => d);
        report.ByDay = days.Select(d => new StatsDayDto
        {
            Date = d.ToDateTime(TimeOnly.MinValue),
            Exams = examsByDay.TryGetValue(d, out var e) ? e.Exams : 0,
            Patients = examsByDay.TryGetValue(d, out var e2) ? e2.Patients : 0,
            Collected = collectedByDay is null ? null : collectedByDay.GetValueOrDefault(d),
        }).ToList();

        if (money && paymentRows is not null)
        {
            report.Money = new MoneySummaryDto
            {
                Billed = active.Where(e => e.InvoiceId != null).Sum(e => e.Price - e.DiscountAmount),
                NotInvoiced = active.Where(e => e.InvoiceId == null).Sum(e => e.Price - e.DiscountAmount),
                Collected = paymentRows.Sum(p => p.Amount),
                Cash = paymentRows.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
                Card = paymentRows.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount),
                Other = paymentRows.Where(p => p.Method == PaymentMethod.Other).Sum(p => p.Amount),
                Discounts = active.Sum(e => e.DiscountAmount),
                DiscountedExams = active.Count(e => e.DiscountAmount > 0),
                FreeExams = active.Count(e => e.DiscountAmount > 0 && e.Price - e.DiscountAmount == 0),
            };
            report.DiscountsByUser = active.Where(e => e.DiscountAmount > 0)
                .GroupBy(e => e.DiscountedByName ?? "—")
                .Select(g => new DiscountByUserDto
                {
                    UserName = g.Key,
                    Exams = g.Count(),
                    FreeExams = g.Count(e => e.Price - e.DiscountAmount == 0),
                    Amount = g.Sum(e => e.DiscountAmount),
                })
                .OrderByDescending(u => u.Amount).ToList();
            report.CashByUser = paymentRows.GroupBy(p => p.UserName)
                .Select(g => new CashByUserDto
                {
                    UserName = g.Key,
                    Payments = g.Count(),
                    Cash = g.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
                    Card = g.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount),
                    Other = g.Where(p => p.Method == PaymentMethod.Other).Sum(p => p.Amount),
                    Total = g.Sum(p => p.Amount),
                })
                .OrderByDescending(u => u.Total).ToList();
        }

        return report;
    }
}
