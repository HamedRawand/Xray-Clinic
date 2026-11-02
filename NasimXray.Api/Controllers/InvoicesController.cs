using NasimXray.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos;
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
    public async Task<ActionResult<InvoiceListResult>> GetAll(
        [FromQuery] int? patientId, [FromQuery] string? search, [FromQuery] InvoiceStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
    {
        var query = ApplyFilters(db.Invoices.AsNoTracking(), patientId, search, status);

        var total = await query.CountAsync();
        var (currentPage, size) = Paging.Normalize(page, pageSize, total);

        // Footer sums cover the whole filtered set, not only the visible page.
        var sumTotal = await query.SumAsync(i => (decimal?)i.Total) ?? 0m;
        var sumPaid = await query.SelectMany(i => i.Payments).SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var list = await query
            .Include(i => i.Patient)
            .Include(i => i.ExamOrders).ThenInclude(o => o.ExamType)
            .Include(i => i.Payments).ThenInclude(p => p.ReceivedBy)
            .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Skip((currentPage - 1) * size).Take(size)
            .ToListAsync();

        return new InvoiceListResult
        {
            Items = list.Select(ToDto).ToList(),
            TotalCount = total,
            Page = currentPage,
            PageSize = size,
            SumTotal = sumTotal,
            SumPaid = sumPaid,
            SumBalance = sumTotal - sumPaid,
        };
    }

    // Same filters as the list, unpaged (capped): feeds the Excel export of the Invoices screen.
    [HttpGet("export")]
    [Authorize(Policy = nameof(Permission.ViewBilling))]
    public async Task<ActionResult<List<InvoiceDto>>> Export(
        [FromQuery] int? patientId, [FromQuery] string? search, [FromQuery] InvoiceStatus? status)
    {
        var list = await ApplyFilters(db.Invoices.AsNoTracking(), patientId, search, status)
            .Include(i => i.Patient)
            .Include(i => i.ExamOrders).ThenInclude(o => o.ExamType)
            .Include(i => i.Payments).ThenInclude(p => p.ReceivedBy)
            .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Take(ExportRowLimit)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    private const int ExportRowLimit = 20_000;

    private static IQueryable<Invoice> ApplyFilters(IQueryable<Invoice> query, int? patientId, string? search, InvoiceStatus? status)
    {
        if (patientId.HasValue) query = query.Where(i => i.PatientId == patientId.Value);
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);

        // Search by patient name / patient ID, or by invoice number ("12" or "#12").
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{term}%";
            var invoiceId = int.TryParse(term.TrimStart('#'), out var parsed) ? parsed : -1;
            query = query.Where(i =>
                EF.Functions.ILike(i.Patient.FullName, pattern) ||
                (i.Patient.PatientCode != null && EF.Functions.ILike(i.Patient.PatientCode, pattern)) ||
                i.Id == invoiceId);
        }

        return query;
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
        if (orders.Any(o => o.Status == ExamOrderStatus.Cancelled))
            return BadRequest(new { message = "A cancelled exam cannot be invoiced." });
        if (orders.Any(o => o.InvoiceId is not null))
            return BadRequest(new { message = "One or more exam orders are already invoiced." });

        var invoice = new Invoice
        {
            PatientId = request.PatientId,
            Total = orders.Sum(o => o.Price - o.DiscountAmount),
            // Nothing to collect (every exam free) = settled immediately, but still on record.
            Status = orders.Sum(o => o.Price - o.DiscountAmount) == 0 ? InvoiceStatus.Paid : InvoiceStatus.Unpaid,
        };
        db.Invoices.Add(invoice);
        foreach (var order in orders) order.Invoice = invoice;

        await db.SaveChangesAsync();
        await audit.LogAsync(User.GetUserId(), "InvoiceCreated", nameof(Invoice), invoice.Id.ToString(), Currency.Format(invoice.Total));

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
            return BadRequest(new { message = $"Payment exceeds the remaining balance of {Currency.Format(balance)}." });

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
        await audit.LogAsync(User.GetUserId(), "PaymentRecorded", nameof(Payment), payment.Id.ToString(), Currency.Format(request.Amount));

        return ToDto((await LoadInvoice(id))!);
    }

    private Task<Invoice?> LoadInvoice(int id) =>
        db.Invoices.AsNoTracking()
            .Include(i => i.Patient)
            .Include(i => i.ExamOrders).ThenInclude(o => o.ExamType)
            .Include(i => i.Payments).ThenInclude(p => p.ReceivedBy)
            .FirstOrDefaultAsync(i => i.Id == id)!;

    internal static InvoiceDto ToDto(Invoice i)
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
                ExamNumber = o.ExamNumber,
                ExamTypeName = o.ExamType.Name,
                ListPrice = o.Price,
                DiscountAmount = o.DiscountAmount,
                DiscountReason = o.DiscountReason,
                Price = o.Price - o.DiscountAmount,
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
