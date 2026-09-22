namespace NasimXray.Api.Models;

public class Report
{
    public int Id { get; set; }

    public int ExamOrderId { get; set; }
    public ExamOrder ExamOrder { get; set; } = null!;

    public string? Findings { get; set; }
    public string? Impression { get; set; }

    public int ReportedById { get; set; }
    public User ReportedBy { get; set; } = null!;

    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public int? FinalizedById { get; set; }
    public User? FinalizedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<ReportAmendment> Amendments { get; set; } = [];
    public List<ReportMeasurement> Measurements { get; set; } = [];

    public uint RowVersion { get; set; }
}
