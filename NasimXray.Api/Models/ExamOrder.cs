using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Models;

public class ExamOrder
{
    public int Id { get; set; }

    // Short human-readable number (E-000123), assigned right after insert from the row id.
    [MaxLength(20)]
    public string? ExamNumber { get; set; }

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public int ExamTypeId { get; set; }
    public ExamType ExamType { get; set; } = null!;

    public int? ReferringPhysicianId { get; set; }
    public ReferringPhysician? ReferringPhysician { get; set; }

    // Price of the exam type when this exam was booked. Invoices and statistics use this, so later
    // changes to the exam type's list price never rewrite history.
    [Column(TypeName = "numeric(10,2)")]
    public decimal Price { get; set; }

    // Amount waived from Price (Price - DiscountAmount is what the patient is charged; equal = free).
    // Only set while the exam is not yet invoiced; always with a reason and the user who gave it.
    [Column(TypeName = "numeric(10,2)")]
    public decimal DiscountAmount { get; set; }

    [MaxLength(250)]
    public string? DiscountReason { get; set; }

    public int? DiscountedById { get; set; }
    public User? DiscountedBy { get; set; }
    public DateTime? DiscountedAt { get; set; }

    public ExamOrderStatus Status { get; set; } = ExamOrderStatus.Pending;

    public int RequestedById { get; set; }
    public User RequestedBy { get; set; } = null!;

    // Set when the exam moves to Completed (the technician who finished it); cleared if it is reopened.
    public int? PerformedById { get; set; }
    public User? PerformedBy { get; set; }
    public DateTime? PerformedAt { get; set; }

    [MaxLength(250)]
    public string? CancelReason { get; set; }

    public Report? Report { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public uint RowVersion { get; set; }
}
