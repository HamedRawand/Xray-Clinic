namespace NasimXray.Api.Models;

public class ReportAmendment
{
    public int Id { get; set; }

    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;

    // Full snapshot of Findings/Impression before this amendment was applied.
    public string? PreviousFindings { get; set; }
    public string? PreviousImpression { get; set; }

    public string Reason { get; set; } = string.Empty;

    public int AmendedById { get; set; }
    public User AmendedBy { get; set; } = null!;

    public DateTime AmendedAt { get; set; } = DateTime.UtcNow;
}
