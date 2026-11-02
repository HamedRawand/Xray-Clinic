using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.Patients;
using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.CheckIn;

// One reception visit, saved all-or-nothing: (new) patient + exam orders + optional invoice + optional payment.
public class CheckInRequest
{
    // Exactly one of these: an existing patient, or the details of a new one.
    public int? PatientId { get; set; }
    public PatientUpsertRequest? NewPatient { get; set; }

    public List<CheckInExamItem> Exams { get; set; } = [];
    public int? ReferringPhysicianId { get; set; }

    public bool CreateInvoice { get; set; } = true;

    // Collected now; 0 leaves the invoice unpaid. Needs CreateInvoice.
    public decimal PaymentAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
}

public class CheckInExamItem
{
    public int ExamTypeId { get; set; }

    // null = full list price. Anything lower is a discount (0 = free) and needs the ApplyDiscount
    // permission and a reason.
    public decimal? Charge { get; set; }
    public string? DiscountReason { get; set; }
}

public class CheckInResponse
{
    public PatientDto Patient { get; set; } = new();
    public bool PatientCreated { get; set; }
    public List<ExamOrderDto> Exams { get; set; } = [];
    public InvoiceDto? Invoice { get; set; }
}
