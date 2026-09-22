using System.ComponentModel.DataAnnotations.Schema;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Models;

public class Payment
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    [Column(TypeName = "numeric(10,2)")]
    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    public int ReceivedById { get; set; }
    public User ReceivedBy { get; set; } = null!;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
