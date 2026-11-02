using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.ExamOrders;

public enum ExamBillingFilter
{
    Any,
    NotInvoiced,
    Unpaid,
    Paid,
    // Any exam that was discounted (including free ones) / waived completely.
    Discounted,
    Free,
}

// All filters are optional and combine with AND. DateFrom/DateTo are inclusive calendar dates in the
// clinic's local time zone (the server converts them to UTC bounds) — never UTC dates, so "today"
// means the clinic's today even late in the evening.
public class ExamOrderSearchQuery
{
    // Matches patient name, patient ID (P-00012), phone number or exam number (E-000123).
    public string? Search { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public int? ExamTypeId { get; set; }
    public string? Modality { get; set; }
    public ExamOrderStatus? Status { get; set; }
    public int? ReferringPhysicianId { get; set; }
    public ExamBillingFilter Billing { get; set; } = ExamBillingFilter.Any;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class ExamTypeCountDto
{
    public int ExamTypeId { get; set; }
    public string ExamTypeName { get; set; } = string.Empty;
    public string? Modality { get; set; }
    public int ExamCount { get; set; }
    public int PatientCount { get; set; }
}

public class ExamOrderSearchResponse
{
    public List<ExamOrderDto> Items { get; set; } = [];
    // Totals and the per-type breakdown cover every exam matching the filters, not just this page.
    public int TotalCount { get; set; }
    public int DistinctPatients { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<ExamTypeCountDto> ByExamType { get; set; } = [];
}
