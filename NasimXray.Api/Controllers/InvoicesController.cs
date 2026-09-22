using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewBilling))]
    public async Task<ActionResult<List<InvoiceDto>>> GetAll([FromQuery] int? patientId)
    {
        var query = db.Invoices.AsNoTracking()
            .Include(i => i.Patient)
            .Include(i => i.ExamOrders).ThenInclude(o => o.ExamType)
            .Include(i => i.Payments).ThenInclude(p => p.ReceivedBy)
            .AsQueryable();

        if (patientId.HasValue) query = query.Where(i => i.PatientId == patientId.Value);

        var list = await query.OrderByDescending(i => i.CreatedAt).Take(500).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    [HttpGet("{id}")]
    [Authorize(Policy = nameof(Permission.ViewBilling))]
    public async Task<ActionResult<InvoiceDto>> GetById(int id)
    {
        var invoice = await LoadInvoice(id);
        return invoice is null ? NotFound() : ToDto(invoice);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageBilling))]
    public async Task<ActionResult<InvoiceDto>> Create(InvoiceCreateRequest request)
    {
        if (request.ExamOrderIds.Count == 0)
            return BadRequest(new { message = "Select at least one exam order to invoice." });

        var orders = await db.ExamOrders.Include(o => o.ExamType)
            .Where(o => request.ExamOrderIds.Contains(o.Id))
            .ToListAsync();

        if (orders.Count != request.ExamOrderIds.Count)
            return BadRequest(new { message = "One or more exam orders were not found." });
        if (orders.Any(o => o.PatientId != request.PatientId))
            return BadRequest(new { message = "All exam orders must belong to the same patient." });
        if (orders.Any(o => o.InvoiceId is not null))
            return BadRequest(new { message = "One or more exam orders are already invoiced." });

        var invoice = new Invoice
        {
            PatientId = request.PatientId,
            Total = orders.Sum(o => o.ExamType.Price),
        };
        db.Invoices.Add(invoice);
        foreach (var order in orders) order.Invoice = invoice;

        await db.SaveChangesAsync();
        await audit.LogAsync(User.GetUserId(), "InvoiceCreated", nameof(Invoice), invoice.Id.ToString(), invoice.Total.ToString("N2"));

        return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, ToDto((await LoadInvoice(invoice.Id))!));
    }

    [HttpPost("{id}/payments")]
    [Authorize(Policy = nameof(Permission.ProcessPayment))]
    public async Task<ActionResult<InvoiceDto>> RecordPayment(int id, PaymentCreateRequest request)
    {
        var invoice = await db.Invoices.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        if (request.Amount <= 0) return BadRequest(new { message = "Payment amount must be greater than zero." });

        var alreadyPaid = invoice.Payments.Sum(p => p.Amount);
        var balance = invoice.Total - alreadyPaid;
        if (request.Amount > balance)
            return BadRequest(new { message = $"Payment exceeds the remaining balance of {balance:N2}." });

        var payment = new Payment
        {
            InvoiceId = id,
            Amount = request.Amount,
            Method = request.Method,
            ReceivedById = User.GetUserId(),
        };
        db.Payments.Add(payment);

        var newPaid = alreadyPaid + request.Amount;
        invoice.Status = newPaid >= invoice.Total ? InvoiceStatus.Paid
            : newPaid > 0 ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Unpaid;

        await db.SaveChangesAsync();
        await audit.LogAsync(User.GetUserId(), "PaymentRecorded", nameof(Payment), payment.Id.ToString(), request.Amount.ToString("N2"));

        return ToDto((await LoadInvoice(id))!);
    }

    private Task<Invoice?> LoadInvoice(int id) =>
        db.Invoices.AsNoTracking()
            .Include(i => i.Patient)
            .Include(i => i.ExamOrders).ThenInclude(o => o.ExamType)
            .Include(i => i.Payments).ThenInclude(p => p.ReceivedBy)
            .FirstOrDefaultAsync(i => i.Id == id)!;

    private static InvoiceDto ToDto(Invoice i)
    {
        var paid = i.Payments.Sum(p => p.Amount);
        return new InvoiceDto
        {
            Id = i.Id,
            PatientId = i.PatientId,
            PatientName = i.Patient.FullName,
            PatientCode = i.Patient.PatientCode,
            Total = i.Total,
            AmountPaid = paid,
            Balance = i.Total - paid,
            Status = i.Status,
            CreatedAt = i.CreatedAt,
            RowVersion = i.RowVersion,
            Lines = i.ExamOrders.Select(o => new InvoiceLineDto
            {
                ExamOrderId = o.Id,
                ExamTypeName = o.ExamType.Name,
                Price = o.ExamType.Price,
            }).ToList(),
            Payments = i.Payments.OrderByDescending(p => p.Timestamp).Select(p => new PaymentDto
            {
                Id = p.Id,
                Amount = p.Amount,
                Method = p.Method,
                ReceivedByName = p.ReceivedBy.FullName,
                Timestamp = p.Timestamp,
            }).ToList(),
        };
    }
}
