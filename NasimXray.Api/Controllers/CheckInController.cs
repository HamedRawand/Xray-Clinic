using NasimXray.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos.CheckIn;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

// Reception check-in: one request creates everything for a visit inside a single database
// transaction, so a failure part-way (e.g. a rejected payment) never leaves a half-registered visit.
[ApiController]
[Route("api/check-in")]
[Authorize(Policy = nameof(Permission.ManageExamOrders))]
public class CheckInController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CheckInResponse>> CheckIn(CheckInRequest request)
    {
        // ── Validate everything up front (and check the extra permissions each part needs) ──
        if (request.PatientId.HasValue == (request.NewPatient is not null))
            return BadRequest(new { message = "Choose an existing patient or enter a new one." });
        if (request.NewPatient is not null)
        {
            if (!Can(Permission.ManagePatients)) return Forbid();
            if (string.IsNullOrWhiteSpace(request.NewPatient.FullName))
                return BadRequest(new { message = "The patient's full name is required." });
        }

        if (request.Exams.Count == 0)
            return BadRequest(new { message = "Select at least one exam." });

        if (request.CreateInvoice && !Can(Permission.ManageBilling)) return Forbid();
        if (request.PaymentAmount != 0)
        {
            if (!request.CreateInvoice)
                return BadRequest(new { message = "A payment needs an invoice." });
            if (!Can(Permission.ProcessPayment)) return Forbid();
            if (request.PaymentAmount < 0)
                return BadRequest(new { message = "Payment amount cannot be negative." });
        }

        Patient? patient = null;
        if (request.PatientId.HasValue)
        {
            patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId.Value);
            if (patient is null) return BadRequest(new { message = "Patient not found." });
        }

        var distinctTypeIds = request.Exams.Select(e => e.ExamTypeId).Distinct().ToList();
        var examTypes = await db.ExamTypes.Where(t => distinctTypeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        if (examTypes.Count != distinctTypeIds.Count)
            return BadRequest(new { message = "One or more exam types were not found." });
        if (examTypes.Values.Any(t => !t.IsActive))
            return BadRequest(new { message = "One or more selected exam types are no longer active." });

        if (request.ReferringPhysicianId.HasValue &&
            !await db.ReferringPhysicians.AnyAsync(p => p.Id == request.ReferringPhysicianId.Value))
            return BadRequest(new { message = "Referring physician not found." });

        // Per-exam charge: no value = list price; anything lower is a discount (0 = free), which needs the
        // ApplyDiscount permission and a reason.
        var canDiscount = Can(Permission.ApplyDiscount);
        var lines = new List<(ExamType Type, decimal Discount, string? Reason)>();
        foreach (var item in request.Exams)
        {
            var type = examTypes[item.ExamTypeId];
            var charge = item.Charge ?? type.Price;
            if (charge < 0 || charge > type.Price)
                return BadRequest(new { message = $"The charge for {type.Name} must be between 0 and {Currency.Format(type.Price)}." });

            var discount = type.Price - charge;
            string? reason = null;
            if (discount > 0)
            {
                if (!canDiscount) return Forbid();
                reason = item.DiscountReason?.Trim();
                if (string.IsNullOrEmpty(reason))
                    return BadRequest(new { message = $"A reason is required for the discount on {type.Name}." });
                if (reason.Length > 250)
                    return BadRequest(new { message = "The discount reason is too long (250 characters max)." });
            }
            lines.Add((type, discount, reason));
        }

        var total = lines.Sum(l => l.Type.Price - l.Discount);
        if (request.PaymentAmount > total)
            return BadRequest(new { message = $"Payment exceeds the total of {Currency.Format(total)}." });

        var userId = User.GetUserId();

        // ── Write it all atomically ──
        await using var tx = await db.Database.BeginTransactionAsync();

        var patientCreated = false;
        if (patient is null)
        {
            var np = request.NewPatient!;
            patient = new Patient
            {
                FullName = np.FullName.Trim(),
                DateOfBirth = np.DateOfBirth,
                Sex = np.Sex,
                PhoneNumber = np.PhoneNumber,
                Address = np.Address,
            };
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
            patient.PatientCode = $"P-{patient.Id:D5}";
            patientCreated = true;
        }

        var now = DateTime.UtcNow;
        var orders = lines.Select(l => new ExamOrder
        {
            PatientId = patient.Id,
            ExamTypeId = l.Type.Id,
            Price = l.Type.Price,
            DiscountAmount = l.Discount,
            DiscountReason = l.Reason,
            DiscountedById = l.Discount > 0 ? userId : null,
            DiscountedAt = l.Discount > 0 ? now : null,
            ReferringPhysicianId = request.ReferringPhysicianId,
            RequestedById = userId,
            Status = ExamOrderStatus.Pending,
        }).ToList();
        db.ExamOrders.AddRange(orders);
        await db.SaveChangesAsync();
        foreach (var order in orders) order.ExamNumber = $"E-{order.Id:D6}";

        Invoice? invoice = null;
        Payment? payment = null;
        if (request.CreateInvoice)
        {
            invoice = new Invoice { PatientId = patient.Id, Total = total, Status = total == 0 ? InvoiceStatus.Paid : InvoiceStatus.Unpaid };
            db.Invoices.Add(invoice);
            foreach (var order in orders) order.Invoice = invoice;

            if (request.PaymentAmount > 0)
            {
                payment = new Payment
                {
                    Invoice = invoice,
                    Amount = request.PaymentAmount,
                    Method = request.PaymentMethod,
                    ReceivedById = userId,
                };
                db.Payments.Add(payment);
                invoice.Status = request.PaymentAmount >= total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        // ── Audit trail uses the same action names as the individual endpoints ──
        if (patientCreated)
            await audit.LogAsync(userId, "PatientCreated", nameof(Patient), patient.Id.ToString(), patient.FullName);
        foreach (var order in orders)
            await audit.LogAsync(userId, "ExamOrderCreated", nameof(ExamOrder), order.Id.ToString(),
                $"{order.ExamNumber} · patient {order.PatientId} · exam type {order.ExamTypeId}");
        foreach (var order in orders.Where(o => o.DiscountAmount > 0))
            await audit.LogAsync(userId, "ExamDiscountApplied", nameof(ExamOrder), order.Id.ToString(),
                $"{order.ExamNumber}: list {Currency.Format(order.Price)} → charge {Currency.Format(order.Price - order.DiscountAmount)} (discount {Currency.Format(order.DiscountAmount)}) — {order.DiscountReason}");
        if (invoice is not null)
            await audit.LogAsync(userId, "InvoiceCreated", nameof(Invoice), invoice.Id.ToString(), Currency.Format(invoice.Total));
        if (payment is not null)
            await audit.LogAsync(userId, "PaymentRecorded", nameof(Payment), payment.Id.ToString(), Currency.Format(payment.Amount));

        // ── Reload with navigation properties for the response ──
        var orderIds = orders.Select(o => o.Id).ToList();
        var loadedOrders = await db.ExamOrders.AsNoTracking()
            .Include(o => o.Patient).Include(o => o.ExamType).Include(o => o.ReferringPhysician)
            .Include(o => o.RequestedBy).Include(o => o.Report).Include(o => o.Invoice).Include(o => o.PerformedBy).Include(o => o.DiscountedBy)
            .Where(o => orderIds.Contains(o.Id))
            .OrderBy(o => o.Id)
            .ToListAsync();

        InvoiceDto? invoiceDto = null;
        if (invoice is not null)
        {
            var loadedInvoice = await db.Invoices.AsNoTracking()
                .Include(i => i.Patient)
                .Include(i => i.ExamOrders).ThenInclude(o => o.ExamType)
                .Include(i => i.Payments).ThenInclude(p => p.ReceivedBy)
                .FirstAsync(i => i.Id == invoice.Id);
            invoiceDto = InvoicesController.ToDto(loadedInvoice);
        }

        return new CheckInResponse
        {
            Patient = PatientsController.ToDto(patient),
            PatientCreated = patientCreated,
            Exams = loadedOrders.Select(ExamOrdersController.ToDto).ToList(),
            Invoice = invoiceDto,
        };
    }

    private bool Can(Permission permission) => User.HasClaim("permission", permission.ToString());
}
