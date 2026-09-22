using System.ComponentModel.DataAnnotations.Schema;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Models;

public class Invoice
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public List<ExamOrder> ExamOrders { get; set; } = [];

    [Column(TypeName = "numeric(10,2)")]
    public decimal Total { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

    public List<Payment> Payments { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public uint RowVersion { get; set; }
}
