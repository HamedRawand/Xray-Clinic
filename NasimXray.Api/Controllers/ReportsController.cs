using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Reports;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/exam-orders/{examOrderId}/report")]
[Authorize]
public class ReportsController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<ReportDto>> Get(int examOrderId)
    {
        var report = await LoadReport(examOrderId);
        return report is null ? NotFound() : ToDto(report);
    }

    [HttpPut]
    [Authorize(Policy = nameof(Permission.CreateReport))]
    public async Task<ActionResult<ReportDto>> SaveDraft(int examOrderId, ReportSaveRequest request)
    {
        var order = await db.ExamOrders.FirstOrDefaultAsync(o => o.Id == examOrderId);
        if (order is null) return NotFound(new { message = "Exam order not found." });

        var report = await db.Reports.Include(r => r.Measurements)
            .FirstOrDefaultAsync(r => r.ExamOrderId == examOrderId);

        if (report is not null && report.IsFinalized)
            return BadRequest(new { message = "This report is finalized. Use Amend to change it." });

        var validFieldIds = await db.MeasurementFieldDefinitions
            .Where(f => f.ExamTypeId == order.ExamTypeId)
            .Select(f => f.Id)
            .ToListAsync();
        if (request.Measurements.Any(m => !validFieldIds.Contains(m.FieldDefinitionId)))
            return BadRequest(new { message = "One or more measurement fields do not belong to this exam type." });

        if (report is null)
        {
            report = new Report { ExamOrderId = examOrderId, ReportedById = User.GetUserId() };
            db.Reports.Add(report);
        }
        else
        {
            if (request.RowVersion.HasValue)
                db.Entry(report).Property(r => r.RowVersion).OriginalValue = request.RowVersion.Value;
        }

        report.Findings = request.Findings;
        report.Impression = request.Impression;
        report.UpdatedAt = DateTime.UtcNow;

        UpsertMeasurements(report, request.Measurements);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This report was changed by someone else. Reload and try again." });
        }

        await audit.LogAsync(User.GetUserId(), "ReportSaved", nameof(Report), report.Id.ToString());
        return ToDto(await LoadReport(examOrderId) ?? report);
    }

    [HttpPost("finalize")]
    [Authorize(Policy = nameof(Permission.FinalizeReport))]
    public async Task<ActionResult<ReportDto>> Finalize(int examOrderId, ReportFinalizeRequest request)
    {
        var report = await db.Reports.Include(r => r.ExamOrder).FirstOrDefaultAsync(r => r.ExamOrderId == examOrderId);
        if (report is null) return NotFound(new { message = "No draft report to finalize." });
        if (report.IsFinalized) return BadRequest(new { message = "Report is already finalized." });

        db.Entry(report).Property(r => r.RowVersion).OriginalValue = request.RowVersion;

        var userId = User.GetUserId();
        report.IsFinalized = true;
        report.FinalizedAt = DateTime.UtcNow;
        report.FinalizedById = userId;
        report.ExamOrder.Status = ExamOrderStatus.Reported;
        report.ExamOrder.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This report was changed by someone else. Reload and try again." });
        }

        await audit.LogAsync(userId, "ReportFinalized", nameof(Report), report.Id.ToString());
        return ToDto(await LoadReport(examOrderId) ?? report);
    }

    [HttpPost("amend")]
    [Authorize(Policy = nameof(Permission.AmendReport))]
    public async Task<ActionResult<ReportDto>> Amend(int examOrderId, ReportAmendRequest request)
    {
        var report = await db.Reports.Include(r => r.Measurements).Include(r => r.ExamOrder)
            .FirstOrDefaultAsync(r => r.ExamOrderId == examOrderId);
        if (report is null) return NotFound();
        if (!report.IsFinalized) return BadRequest(new { message = "Only a finalized report can be amended — use Save instead." });
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { message = "A reason is required to amend a finalized report." });

        var validFieldIds = await db.MeasurementFieldDefinitions
            .Where(f => f.ExamTypeId == report.ExamOrder.ExamTypeId)
            .Select(f => f.Id)
            .ToListAsync();
        if (request.Measurements.Any(m => !validFieldIds.Contains(m.FieldDefinitionId)))
            return BadRequest(new { message = "One or more measurement fields do not belong to this exam type." });

        db.Entry(report).Property(r => r.RowVersion).OriginalValue = request.RowVersion;

        var userId = User.GetUserId();
        db.ReportAmendments.Add(new ReportAmendment
        {
            ReportId = report.Id,
            PreviousFindings = report.Findings,
            PreviousImpression = report.Impression,
            Reason = request.Reason,
            AmendedById = userId,
        });

        report.Findings = request.Findings;
        report.Impression = request.Impression;
        report.UpdatedAt = DateTime.UtcNow;
        UpsertMeasurements(report, request.Measurements);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This report was changed by someone else. Reload and try again." });
        }

        await audit.LogAsync(userId, "ReportAmended", nameof(Report), report.Id.ToString(), request.Reason);
        return ToDto(await LoadReport(examOrderId) ?? report);
    }

    private void UpsertMeasurements(Report report, List<MeasurementValueRequest> values)
    {
        foreach (var value in values)
        {
            var existing = report.Measurements.FirstOrDefault(m => m.FieldDefinitionId == value.FieldDefinitionId);
            if (existing is not null)
                existing.Value = value.Value;
            else
                report.Measurements.Add(new ReportMeasurement { FieldDefinitionId = value.FieldDefinitionId, Value = value.Value });
        }
    }

    private Task<Report?> LoadReport(int examOrderId) =>
        db.Reports.AsNoTracking()
            .Include(r => r.ReportedBy)
            .Include(r => r.FinalizedBy)
            .Include(r => r.Measurements).ThenInclude(m => m.FieldDefinition)
            .Include(r => r.Amendments).ThenInclude(a => a.AmendedBy)
            .FirstOrDefaultAsync(r => r.ExamOrderId == examOrderId)!;

    private static ReportDto ToDto(Report r) => new()
    {
        Id = r.Id,
        ExamOrderId = r.ExamOrderId,
        Findings = r.Findings,
        Impression = r.Impression,
        ReportedById = r.ReportedById,
        ReportedByName = r.ReportedBy.FullName,
        IsFinalized = r.IsFinalized,
        FinalizedAt = r.FinalizedAt,
        FinalizedByName = r.FinalizedBy?.FullName,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        RowVersion = r.RowVersion,
        Measurements = r.Measurements.Select(m => new MeasurementValueDto
        {
            FieldDefinitionId = m.FieldDefinitionId,
            FieldKey = m.FieldDefinition.FieldKey,
            Label = m.FieldDefinition.Label,
            Unit = m.FieldDefinition.Unit,
            DataType = m.FieldDefinition.DataType,
            Value = m.Value,
        }).OrderBy(m => m.Label).ToList(),
        Amendments = r.Amendments.OrderByDescending(a => a.AmendedAt).Select(a => new ReportAmendmentDto
        {
            Id = a.Id,
            PreviousFindings = a.PreviousFindings,
            PreviousImpression = a.PreviousImpression,
            Reason = a.Reason,
            AmendedByName = a.AmendedBy.FullName,
            AmendedAt = a.AmendedAt,
        }).ToList(),
    };
}
