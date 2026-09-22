using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.ExamOrders;

public class ExamOrderDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PatientCode { get; set; }
    public int ExamTypeId { get; set; }
    public string ExamTypeName { get; set; } = string.Empty;
    public string? Modality { get; set; }
    public int? ReferringPhysicianId { get; set; }
    public string? ReferringPhysicianName { get; set; }
    public ExamOrderStatus Status { get; set; }
    public int RequestedById { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public bool HasReport { get; set; }
    public int? InvoiceId { get; set; }
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

public class ExamOrderStatusUpdateRequest
{
    public ExamOrderStatus Status { get; set; }
    public uint RowVersion { get; set; }
}
