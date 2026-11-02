using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.Billing;

public class InvoiceLineDto
{
    public int ExamOrderId { get; set; }
    public string? ExamNumber { get; set; }
    public string ExamTypeName { get; set; } = string.Empty;
    public decimal ListPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? DiscountReason { get; set; }
    // What was actually charged for this line (ListPrice - DiscountAmount).
    public decimal Price { get; set; }
}

public class PaymentDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string ReceivedByName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class InvoiceDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PatientCode { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public uint RowVersion { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = [];
    public List<PaymentDto> Payments { get; set; } = [];
}

public class InvoiceCreateRequest
{
    public int PatientId { get; set; }
    public List<int> ExamOrderIds { get; set; } = [];
}

public class PaymentCreateRequest
{
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
}
