using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.ExamTypes;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/exam-types")]
[Authorize]
public class ExamTypesController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ExamTypeDto>>> GetAll([FromQuery] bool includeInactive = false)
    {
        var query = db.ExamTypes.AsNoTracking().Include(t => t.MeasurementFields).AsQueryable();
        if (!includeInactive) query = query.Where(t => t.IsActive);
        var list = await query.OrderBy(t => t.Modality).ThenBy(t => t.Name).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageExamTypes))]
    public async Task<ActionResult<ExamTypeDto>> Create(ExamTypeUpsertRequest request)
    {
        var entity = new ExamType
        {
            Name = request.Name,
            Modality = request.Modality,
            BodyPart = request.BodyPart,
            Price = request.Price,
            IsActive = request.IsActive,
        };
        db.ExamTypes.Add(entity);
        await db.SaveChangesAsync();
        await audit.LogAsync(User.GetUserId(), "ExamTypeCreated", nameof(ExamType), entity.Id.ToString(), entity.Name);
        return CreatedAtAction(nameof(GetAll), ToDto(entity));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = nameof(Permission.ManageExamTypes))]
    public async Task<ActionResult<ExamTypeDto>> Update(int id, ExamTypeUpsertRequest request)
    {
        var entity = await db.ExamTypes.Include(t => t.MeasurementFields).FirstOrDefaultAsync(t => t.Id == id);
        if (entity is null) return NotFound();

        entity.Name = request.Name;
        entity.Modality = request.Modality;
        entity.BodyPart = request.BodyPart;
        entity.Price = request.Price;
        entity.IsActive = request.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "ExamTypeUpdated", nameof(ExamType), entity.Id.ToString(), entity.Name);
        return ToDto(entity);
    }

    // ── Measurement field definitions (§6.2 — admin-configurable structured fields) ──

    [HttpPost("{examTypeId}/measurement-fields")]
    [Authorize(Policy = nameof(Permission.ManageExamTypes))]
    public async Task<ActionResult<MeasurementFieldDto>> AddField(int examTypeId, MeasurementFieldUpsertRequest request)
    {
        var examType = await db.ExamTypes.FirstOrDefaultAsync(t => t.Id == examTypeId);
        if (examType is null) return NotFound();

        var field = new MeasurementFieldDefinition
        {
            ExamTypeId = examTypeId,
            FieldKey = request.FieldKey,
            Label = request.Label,
            Unit = request.Unit,
            DataType = request.DataType,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
        };
        db.MeasurementFieldDefinitions.Add(field);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "MeasurementFieldCreated", nameof(MeasurementFieldDefinition), field.Id.ToString(), $"{examType.Name}: {field.Label}");
        return ToFieldDto(field);
    }

    [HttpPut("{examTypeId}/measurement-fields/{fieldId}")]
    [Authorize(Policy = nameof(Permission.ManageExamTypes))]
    public async Task<ActionResult<MeasurementFieldDto>> UpdateField(int examTypeId, int fieldId, MeasurementFieldUpsertRequest request)
    {
        var field = await db.MeasurementFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId && f.ExamTypeId == examTypeId);
        if (field is null) return NotFound();

        field.FieldKey = request.FieldKey;
        field.Label = request.Label;
        field.Unit = request.Unit;
        field.DataType = request.DataType;
        field.DisplayOrder = request.DisplayOrder;
        field.IsActive = request.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "MeasurementFieldUpdated", nameof(MeasurementFieldDefinition), field.Id.ToString(), field.Label);
        return ToFieldDto(field);
    }

    [HttpDelete("{examTypeId}/measurement-fields/{fieldId}")]
    [Authorize(Policy = nameof(Permission.ManageExamTypes))]
    public async Task<IActionResult> DeleteField(int examTypeId, int fieldId)
    {
        var field = await db.MeasurementFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId && f.ExamTypeId == examTypeId);
        if (field is null) return NotFound();

        // Soft delete — a hard delete would orphan any ReportMeasurement rows already recorded
        // against this field on finalized (locked) reports.
        field.IsActive = false;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "MeasurementFieldDeactivated", nameof(MeasurementFieldDefinition), field.Id.ToString(), field.Label);
        return NoContent();
    }

    private static MeasurementFieldDto ToFieldDto(MeasurementFieldDefinition f) => new()
    {
        Id = f.Id,
        FieldKey = f.FieldKey,
        Label = f.Label,
        Unit = f.Unit,
        DataType = f.DataType,
        DisplayOrder = f.DisplayOrder,
        IsActive = f.IsActive,
    };

    private static ExamTypeDto ToDto(ExamType t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Modality = t.Modality,
        BodyPart = t.BodyPart,
        Price = t.Price,
        IsActive = t.IsActive,
        MeasurementFields = t.MeasurementFields
            .Where(f => f.IsActive)
            .OrderBy(f => f.DisplayOrder)
            .Select(ToFieldDto)
            .ToList(),
    };
}
