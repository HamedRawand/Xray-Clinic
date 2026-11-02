using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.Stats;

// Dates are plain DateTime (time 00:00, Kind Unspecified) holding a clinic-local calendar date — the
// net6.0 client's System.Text.Json has no DateOnly support. Money fields are null when the caller
// lacks the ViewBilling permission, so the UI can hide them.

public class StatusCountDto
{
    public ExamOrderStatus Status { get; set; }
    public int Count { get; set; }
}

public class MoneyTodayDto
{
    public decimal Collected { get; set; }
    public decimal Cash { get; set; }
    public decimal Card { get; set; }
    public decimal Other { get; set; }
    public decimal Invoiced { get; set; }
    public decimal Discounts { get; set; }
    public int FreeExams { get; set; }
    // Unpaid balance across ALL invoices, not just today's.
    public decimal Outstanding { get; set; }
}

public class DashboardDto
{
    public DateTime Date { get; set; }
    public int Exams { get; set; }
    public int Patients { get; set; }
    public int NewPatients { get; set; }
    public int Cancelled { get; set; }
    public List<ExamTypeCountDto> ByExamType { get; set; } = [];
    public List<StatusCountDto> ByStatus { get; set; } = [];
    public ExamQueueCountsDto Queues { get; set; } = new();
    public MoneyTodayDto? Money { get; set; }
}

public class StatsRowDto
{
    public int? Id { get; set; }
    // null = "no referring physician" (the client shows the localized text).
    public string? Label { get; set; }
    public int Exams { get; set; }
    public int Patients { get; set; }
    public decimal? Revenue { get; set; }
    public decimal? Discount { get; set; }
}

public class StatsDayDto
{
    public DateTime Date { get; set; }
    public int Exams { get; set; }
    public int Patients { get; set; }
    public decimal? Collected { get; set; }
}

public class CashByUserDto
{
    public string UserName { get; set; } = string.Empty;
    public int Payments { get; set; }
    public decimal Cash { get; set; }
    public decimal Card { get; set; }
    public decimal Other { get; set; }
    public decimal Total { get; set; }
}

public class MoneySummaryDto
{
    // Value of invoiced / not-yet-invoiced exams performed in the period (current exam prices).
    public decimal Billed { get; set; }
    public decimal NotInvoiced { get; set; }
    // Payments RECEIVED in the period, whatever date the exam was.
    public decimal Collected { get; set; }
    public decimal Cash { get; set; }
    public decimal Card { get; set; }
    public decimal Other { get; set; }
    // Waived value, and how many exams were discounted / given completely free (waived to 0).
    public decimal Discounts { get; set; }
    public int DiscountedExams { get; set; }
    public int FreeExams { get; set; }
}

public class DiscountByUserDto
{
    public string UserName { get; set; } = string.Empty;
    public int Exams { get; set; }
    public int FreeExams { get; set; }
    public decimal Amount { get; set; }
}

public class StatsReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalExams { get; set; }
    public int DistinctPatients { get; set; }
    public int NewPatients { get; set; }
    public int CancelledExams { get; set; }
    public List<StatsRowDto> ByExamType { get; set; } = [];
    public List<StatsRowDto> ByModality { get; set; } = [];
    public List<StatsRowDto> ByReferringPhysician { get; set; } = [];
    public List<StatsDayDto> ByDay { get; set; } = [];
    public MoneySummaryDto? Money { get; set; }
    public List<CashByUserDto>? CashByUser { get; set; }
    public List<DiscountByUserDto>? DiscountsByUser { get; set; }
}
