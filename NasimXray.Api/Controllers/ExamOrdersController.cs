using NasimXray.Shared;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/exam-orders")]
[Authorize]
public class ExamOrdersController(AppDbContext db, AuditService audit, ClinicClock clock) : ControllerBase
{
    // Workflow: Pending → InProgress → Completed, with Cancel before completion and a reopen step back.
    private static readonly Dictionary<ExamOrderStatus, ExamOrderStatus[]> AllowedTransitions = new()
    {
        [ExamOrderStatus.Pending] = [ExamOrderStatus.InProgress, ExamOrderStatus.Cancelled],
        [ExamOrderStatus.InProgress] = [ExamOrderStatus.Completed, ExamOrderStatus.Pending, ExamOrderStatus.Cancelled],
        [ExamOrderStatus.Completed] = [ExamOrderStatus.InProgress],
    };

    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewExamOrders))]
    public async Task<ActionResult<List<ExamOrderDto>>> GetAll([FromQuery] ExamOrderStatus? status, [FromQuery] int? patientId, [FromQuery] bool? uninvoiced)
    {
        var query = db.ExamOrders.AsNoTracking()
            .Include(o => o.Patient)
            .Include(o => o.ExamType)
            .Include(o => o.ReferringPhysician)
            .Include(o => o.RequestedBy)
            .Include(o => o.Report)
            .Include(o => o.Invoice)
            .Include(o => o.PerformedBy)
            .Include(o => o.DiscountedBy)
            .AsQueryable();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (patientId.HasValue) query = query.Where(o => o.PatientId == patientId.Value);
        if (uninvoiced == true) query = query.Where(o => o.InvoiceId == null && o.Status != ExamOrderStatus.Cancelled);

        var list = await query.OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    // Cross-patient exams register: every exam in the clinic's history, filterable and paged.
    [HttpGet("search")]
    [Authorize(Policy = nameof(Permission.ViewExamOrders))]
    public async Task<ActionResult<ExamOrderSearchResponse>> Search([FromQuery] ExamOrderSearchQuery q)
    {
        var query = ApplyFilters(db.ExamOrders.AsNoTracking(), q);

        var page = Math.Max(q.Page, 1);
        var pageSize = Math.Clamp(q.PageSize, 1, 200);

        var totalCount = await query.CountAsync();
        var distinctPatients = await query.Select(o => o.PatientId).Distinct().CountAsync();

        var byType = await query
            .GroupBy(o => new { o.ExamTypeId, o.ExamType.Name, o.ExamType.Modality })
            .Select(g => new ExamTypeCountDto
            {
                ExamTypeId = g.Key.ExamTypeId,
                ExamTypeName = g.Key.Name,
                Modality = g.Key.Modality,
                ExamCount = g.Count(),
                PatientCount = g.Select(o => o.PatientId).Distinct().Count(),
            })
            .OrderByDescending(c => c.ExamCount).ThenBy(c => c.ExamTypeName)
            .ToListAsync();

        var items = await query
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(DtoProjection)
            .ToListAsync();

        return new ExamOrderSearchResponse
        {
            Items = items,
            TotalCount = totalCount,
            DistinctPatients = distinctPatients,
            Page = page,
            PageSize = pageSize,
            ByExamType = byType,
        };
    }

    // Same filters as Search but unpaged (capped) — feeds the Excel export of the register.
    [HttpGet("export")]
    [Authorize(Policy = nameof(Permission.ViewExamOrders))]
    public async Task<ActionResult<List<ExamOrderDto>>> Export([FromQuery] ExamOrderSearchQuery q) =>
        await ApplyFilters(db.ExamOrders.AsNoTracking(), q)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(ExportRowLimit)
            .Select(DtoProjection)
            .ToListAsync();

    private const int ExportRowLimit = 50_000;

    private IQueryable<ExamOrder> ApplyFilters(IQueryable<ExamOrder> query, ExamOrderSearchQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = $"%{q.Search.Trim()}%";
            query = query.Where(o =>
                EF.Functions.ILike(o.Patient.FullName, term) ||
                (o.Patient.PatientCode != null && EF.Functions.ILike(o.Patient.PatientCode, term)) ||
                (o.Patient.PhoneNumber != null && EF.Functions.ILike(o.Patient.PhoneNumber, term)) ||
                (o.ExamNumber != null && EF.Functions.ILike(o.ExamNumber, term)));
        }

        var from = q.DateFrom;
        var to = q.DateTo;
        if (from.HasValue && to.HasValue && from > to) (from, to) = (to, from);
        if (from.HasValue)
        {
            var startUtc = clock.StartOfDayUtc(from.Value);
            query = query.Where(o => o.CreatedAt >= startUtc);
        }
        if (to.HasValue)
        {
            var endUtc = clock.StartOfDayUtc(to.Value.AddDays(1));
            query = query.Where(o => o.CreatedAt < endUtc);
        }

        if (q.ExamTypeId.HasValue) query = query.Where(o => o.ExamTypeId == q.ExamTypeId.Value);
        if (!string.IsNullOrWhiteSpace(q.Modality))
        {
            var modality = q.Modality.Trim();
            query = query.Where(o => o.ExamType.Modality == modality);
        }
        if (q.Status.HasValue) query = query.Where(o => o.Status == q.Status.Value);
        if (q.ReferringPhysicianId.HasValue) query = query.Where(o => o.ReferringPhysicianId == q.ReferringPhysicianId.Value);

        return q.Billing switch
        {
            ExamBillingFilter.NotInvoiced => query.Where(o => o.InvoiceId == null && o.Status != ExamOrderStatus.Cancelled),
            ExamBillingFilter.Unpaid => query.Where(o => o.Invoice != null && o.Invoice.Status != InvoiceStatus.Paid),
            ExamBillingFilter.Paid => query.Where(o => o.Invoice != null && o.Invoice.Status == InvoiceStatus.Paid),
            ExamBillingFilter.Discounted => query.Where(o => o.DiscountAmount > 0 && o.Status != ExamOrderStatus.Cancelled),
            ExamBillingFilter.Free => query.Where(o => o.DiscountAmount > 0 && o.Price - o.DiscountAmount == 0 && o.Status != ExamOrderStatus.Cancelled),
            _ => query,
        };
    }

    private static readonly Expression<Func<ExamOrder, ExamOrderDto>> DtoProjection = o => new ExamOrderDto
    {
        Id = o.Id,
        ExamNumber = o.ExamNumber,
        PatientId = o.PatientId,
        PatientName = o.Patient.FullName,
        PatientCode = o.Patient.PatientCode,
        ExamTypeId = o.ExamTypeId,
        ExamTypeName = o.ExamType.Name,
        Modality = o.ExamType.Modality,
        Price = o.Price,
        ReferringPhysicianId = o.ReferringPhysicianId,
        ReferringPhysicianName = o.ReferringPhysician != null ? o.ReferringPhysician.Name : null,
        DiscountAmount = o.DiscountAmount,
        DiscountReason = o.DiscountReason,
        DiscountedByName = o.DiscountedBy != null ? o.DiscountedBy.FullName : null,
        DiscountedAt = o.DiscountedAt,
        Status = o.Status,
        RequestedById = o.RequestedById,
        RequestedByName = o.RequestedBy.FullName,
        PerformedById = o.PerformedById,
        PerformedByName = o.PerformedBy != null ? o.PerformedBy.FullName : null,
        PerformedAt = o.PerformedAt,
        CancelReason = o.CancelReason,
        HasReport = o.Report != null,
        InvoiceId = o.InvoiceId,
        InvoiceStatus = o.Invoice != null ? o.Invoice.Status : null,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        RowVersion = o.RowVersion,
    };

    [HttpGet("queue-counts")]
    [Authorize(Policy = nameof(Permission.ViewExamOrders))]
    public async Task<ActionResult<ExamQueueCountsDto>> QueueCounts() => await ExamQueueCounts.GetAsync(db);

    [HttpGet("{id}")]
    [Authorize(Policy = nameof(Permission.ViewExamOrders))]
    public async Task<ActionResult<ExamOrderDto>> GetById(int id)
    {
        var order = await db.ExamOrders.AsNoTracking()
            .Include(o => o.Patient)
            .Include(o => o.ExamType)
            .Include(o => o.ReferringPhysician)
            .Include(o => o.RequestedBy)
            .Include(o => o.Report)
            .Include(o => o.Invoice)
            .Include(o => o.PerformedBy)
            .Include(o => o.DiscountedBy)
            .FirstOrDefaultAsync(o => o.Id == id);

        return order is null ? NotFound() : ToDto(order);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageExamOrders))]
    public async Task<ActionResult<ExamOrderDto>> Create(ExamOrderCreateRequest request)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId))
            return BadRequest(new { message = "Patient not found." });
        var examType = await db.ExamTypes.FirstOrDefaultAsync(t => t.Id == request.ExamTypeId);
        if (examType is null)
            return BadRequest(new { message = "Exam type not found." });

        var order = new ExamOrder
        {
            PatientId = request.PatientId,
            ExamTypeId = request.ExamTypeId,
            Price = examType.Price,
            ReferringPhysicianId = request.ReferringPhysicianId,
            RequestedById = User.GetUserId(),
            Status = ExamOrderStatus.Pending,
        };
        db.ExamOrders.Add(order);
        await db.SaveChangesAsync();

        order.ExamNumber = $"E-{order.Id:D6}";
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "ExamOrderCreated", nameof(ExamOrder), order.Id.ToString(),
            $"{order.ExamNumber} · patient {order.PatientId} · exam type {order.ExamTypeId}");

        await db.Entry(order).Reference(o => o.Patient).LoadAsync();
        await db.Entry(order).Reference(o => o.ExamType).LoadAsync();
        await db.Entry(order).Reference(o => o.ReferringPhysician).LoadAsync();
        await db.Entry(order).Reference(o => o.RequestedBy).LoadAsync();

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(order));
    }

    // Discount / free exam. Allowed only before the exam is invoiced (after payment it would need refunds).
    [HttpPut("{id}/discount")]
    [Authorize(Policy = nameof(Permission.ApplyDiscount))]
    public async Task<ActionResult<ExamOrderDto>> UpdateDiscount(int id, ExamDiscountRequest request)
    {
        var order = await db.ExamOrders
            .Include(o => o.Patient)
            .Include(o => o.ExamType)
            .Include(o => o.ReferringPhysician)
            .Include(o => o.RequestedBy)
            .Include(o => o.Report)
            .Include(o => o.Invoice)
            .Include(o => o.PerformedBy)
            .Include(o => o.DiscountedBy)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        if (order.Status == ExamOrderStatus.Cancelled)
            return BadRequest(new { message = "A cancelled exam cannot be discounted." });
        if (order.InvoiceId is not null)
            return BadRequest(new { message = "This exam is already on an invoice, so its price can no longer be changed." });
        if (request.Charge < 0 || request.Charge > order.Price)
            return BadRequest(new { message = $"The charge must be between 0 and {Currency.Format(order.Price)}." });

        var discount = order.Price - request.Charge;
        var reason = request.Reason?.Trim();
        if (discount > 0)
        {
            if (string.IsNullOrEmpty(reason))
                return BadRequest(new { message = "A reason is required for a discount." });
            if (reason.Length > 250)
                return BadRequest(new { message = "The reason is too long (250 characters max)." });
        }

        var previousDiscount = order.DiscountAmount;
        db.Entry(order).Property(o => o.RowVersion).OriginalValue = request.RowVersion;
        order.UpdatedAt = DateTime.UtcNow;
        if (discount > 0)
        {
            order.DiscountAmount = discount;
            order.DiscountReason = reason;
            order.DiscountedById = User.GetUserId();
            order.DiscountedAt = DateTime.UtcNow;
        }
        else
        {
            order.DiscountAmount = 0;
            order.DiscountReason = null;
            order.DiscountedById = null;
            order.DiscountedAt = null;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This exam order was changed by someone else. Reload and try again." });
        }

        if (discount > 0)
            await audit.LogAsync(User.GetUserId(), "ExamDiscountApplied", nameof(ExamOrder), order.Id.ToString(),
                $"{order.ExamNumber}: list {Currency.Format(order.Price)} → charge {Currency.Format(request.Charge)} (discount {Currency.Format(discount)}; was {Currency.Format(previousDiscount)}) — {reason}");
        else if (previousDiscount > 0)
            await audit.LogAsync(User.GetUserId(), "ExamDiscountRemoved", nameof(ExamOrder), order.Id.ToString(),
                $"{order.ExamNumber}: discount of {Currency.Format(previousDiscount)} removed");

        await db.Entry(order).Reference(o => o.DiscountedBy).LoadAsync();
        return ToDto(order);
    }

    [HttpPut("{id}/status")]
    [Authorize(Policy = nameof(Permission.ManageExamOrders))]
    public async Task<ActionResult<ExamOrderDto>> UpdateStatus(int id, ExamOrderStatusUpdateRequest request)
    {
        var order = await db.ExamOrders
            .Include(o => o.Patient)
            .Include(o => o.ExamType)
            .Include(o => o.ReferringPhysician)
            .Include(o => o.RequestedBy)
            .Include(o => o.Report)
            .Include(o => o.Invoice)
            .Include(o => o.PerformedBy)
            .Include(o => o.DiscountedBy)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        // Reported (via report finalize) and Cancelled are terminal: a finalized report is locked and
        // only changes through the audited Amend flow, and a cancelled exam stays cancelled.
        if (!AllowedTransitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(request.Status))
            return BadRequest(new { message = $"An exam that is {order.Status} cannot be changed to {request.Status}." });

        string? cancelReason = null;
        if (request.Status == ExamOrderStatus.Cancelled)
        {
            cancelReason = request.CancelReason?.Trim();
            if (string.IsNullOrEmpty(cancelReason))
                return BadRequest(new { message = "A reason is required to cancel an exam." });
            if (cancelReason.Length > 250)
                return BadRequest(new { message = "The cancellation reason is too long (250 characters max)." });
            if (order.InvoiceId is not null)
                return BadRequest(new { message = "This exam is already on an invoice, so it cannot be cancelled here." });
        }

        var previous = order.Status;
        db.Entry(order).Property(o => o.RowVersion).OriginalValue = request.RowVersion;
        order.Status = request.Status;
        order.UpdatedAt = DateTime.UtcNow;
        order.CancelReason = cancelReason;

        if (request.Status == ExamOrderStatus.Completed)
        {
            order.PerformedById = User.GetUserId();
            order.PerformedAt = DateTime.UtcNow;
        }
        else
        {
            order.PerformedById = null;
            order.PerformedAt = null;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This exam order was changed by someone else. Reload and try again." });
        }

        await audit.LogAsync(User.GetUserId(), "ExamOrderStatusChanged", nameof(ExamOrder), order.Id.ToString(),
            cancelReason is null ? $"{order.ExamNumber}: {previous} → {request.Status}" : $"{order.ExamNumber}: {previous} → {request.Status} ({cancelReason})");
        return ToDto(order);
    }

    internal static ExamOrderDto ToDto(ExamOrder o) => new()
    {
        Id = o.Id,
        ExamNumber = o.ExamNumber,
        PatientId = o.PatientId,
        PatientName = o.Patient.FullName,
        PatientCode = o.Patient.PatientCode,
        ExamTypeId = o.ExamTypeId,
        ExamTypeName = o.ExamType.Name,
        Modality = o.ExamType.Modality,
        Price = o.Price,
        ReferringPhysicianId = o.ReferringPhysicianId,
        ReferringPhysicianName = o.ReferringPhysician?.Name,
        DiscountAmount = o.DiscountAmount,
        DiscountReason = o.DiscountReason,
        DiscountedByName = o.DiscountedBy?.FullName,
        DiscountedAt = o.DiscountedAt,
        Status = o.Status,
        RequestedById = o.RequestedById,
        RequestedByName = o.RequestedBy.FullName,
        PerformedById = o.PerformedById,
        PerformedByName = o.PerformedBy?.FullName,
        PerformedAt = o.PerformedAt,
        CancelReason = o.CancelReason,
        HasReport = o.Report is not null,
        InvoiceId = o.InvoiceId,
        InvoiceStatus = o.Invoice?.Status,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        RowVersion = o.RowVersion,
    };
}
