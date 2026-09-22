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
public class ExamOrdersController(AppDbContext db, AuditService audit) : ControllerBase
{
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
            .AsQueryable();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (patientId.HasValue) query = query.Where(o => o.PatientId == patientId.Value);
        if (uninvoiced == true) query = query.Where(o => o.InvoiceId == null);

        var list = await query.OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync();
        return list.Select(ToDto).ToList();
    }

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
            .FirstOrDefaultAsync(o => o.Id == id);

        return order is null ? NotFound() : ToDto(order);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageExamOrders))]
    public async Task<ActionResult<ExamOrderDto>> Create(ExamOrderCreateRequest request)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId))
            return BadRequest(new { message = "Patient not found." });
        if (!await db.ExamTypes.AnyAsync(t => t.Id == request.ExamTypeId))
            return BadRequest(new { message = "Exam type not found." });

        var order = new ExamOrder
        {
            PatientId = request.PatientId,
            ExamTypeId = request.ExamTypeId,
            ReferringPhysicianId = request.ReferringPhysicianId,
            RequestedById = User.GetUserId(),
            Status = ExamOrderStatus.Pending,
        };
        db.ExamOrders.Add(order);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "ExamOrderCreated", nameof(ExamOrder), order.Id.ToString());

        await db.Entry(order).Reference(o => o.Patient).LoadAsync();
        await db.Entry(order).Reference(o => o.ExamType).LoadAsync();
        await db.Entry(order).Reference(o => o.ReferringPhysician).LoadAsync();
        await db.Entry(order).Reference(o => o.RequestedBy).LoadAsync();

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(order));
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
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        // Reported/Cancelled are terminal states reached via the report-finalize flow or an
        // explicit cancel — this endpoint only handles the Pending/InProgress/Completed handoff.
        if (order.Status is ExamOrderStatus.Reported)
            return BadRequest(new { message = "This exam already has a finalized report and cannot have its status changed directly." });

        db.Entry(order).Property(o => o.RowVersion).OriginalValue = request.RowVersion;
        order.Status = request.Status;
        order.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This exam order was changed by someone else. Reload and try again." });
        }

        await audit.LogAsync(User.GetUserId(), "ExamOrderStatusChanged", nameof(ExamOrder), order.Id.ToString(), request.Status.ToString());
        return ToDto(order);
    }

    private static ExamOrderDto ToDto(ExamOrder o) => new()
    {
        Id = o.Id,
        PatientId = o.PatientId,
        PatientName = o.Patient.FullName,
        PatientCode = o.Patient.PatientCode,
        ExamTypeId = o.ExamTypeId,
        ExamTypeName = o.ExamType.Name,
        Modality = o.ExamType.Modality,
        ReferringPhysicianId = o.ReferringPhysicianId,
        ReferringPhysicianName = o.ReferringPhysician?.Name,
        Status = o.Status,
        RequestedById = o.RequestedById,
        RequestedByName = o.RequestedBy.FullName,
        HasReport = o.Report is not null,
        InvoiceId = o.InvoiceId,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        RowVersion = o.RowVersion,
    };
}
