using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.ExamOrders;

public class ExamOrderDto
{
    public int Id { get; set; }
    public string? ExamNumber { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PatientCode { get; set; }
    public int ExamTypeId { get; set; }
    public string ExamTypeName { get; set; } = string.Empty;
    public string? Modality { get; set; }
    // List price when booked; the patient is charged NetPrice = Price - DiscountAmount.
    public decimal Price { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? DiscountReason { get; set; }
    public string? DiscountedByName { get; set; }
    public DateTime? DiscountedAt { get; set; }
    public decimal NetPrice => Price - DiscountAmount;
    public bool IsDiscounted => DiscountAmount > 0;
    // A discount can only be set or removed before the exam is invoiced.
    public bool IsDiscountable => InvoiceId == null && Status != ExamOrderStatus.Cancelled;
    public int? ReferringPhysicianId { get; set; }
    public string? ReferringPhysicianName { get; set; }
    public ExamOrderStatus Status { get; set; }
    public int RequestedById { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public int? PerformedById { get; set; }
    public string? PerformedByName { get; set; }
    public DateTime? PerformedAt { get; set; }
    public string? CancelReason { get; set; }
    public bool HasReport { get; set; }
    public int? InvoiceId { get; set; }
    public InvoiceStatus? InvoiceStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public uint RowVersion { get; set; }
}

public class ExamOrderCreateRequest
{
    public int PatientId { get; set; }
    public int ExamTypeId { get; set; }
    public int? ReferringPhysicianId { get; set; }
}

public class ExamDiscountRequest
{
    // What the patient will be charged for this exam: 0 = free, equal to the list price = no discount.
    public decimal Charge { get; set; }
    public string? Reason { get; set; }
    public uint RowVersion { get; set; }
}

public class ExamOrderStatusUpdateRequest
{
    public ExamOrderStatus Status { get; set; }
    public uint RowVersion { get; set; }

    // Required (non-blank) when Status is Cancelled.
    public string? CancelReason { get; set; }
}
