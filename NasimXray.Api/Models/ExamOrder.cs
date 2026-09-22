using NasimXray.Shared.Enums;

namespace NasimXray.Api.Models;

public class ExamOrder
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public int ExamTypeId { get; set; }
    public ExamType ExamType { get; set; } = null!;

    public int? ReferringPhysicianId { get; set; }
    public ReferringPhysician? ReferringPhysician { get; set; }

    public ExamOrderStatus Status { get; set; } = ExamOrderStatus.Pending;

    public int RequestedById { get; set; }
    public User RequestedBy { get; set; } = null!;

    public Report? Report { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public uint RowVersion { get; set; }
}
