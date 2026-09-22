using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Patients;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientsController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewPatients))]
    public async Task<ActionResult<List<PatientDto>>> GetAll([FromQuery] string? search)
    {
        var query = db.Patients.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.FullName, $"%{term}%") ||
                (p.PatientCode != null && EF.Functions.ILike(p.PatientCode, $"%{term}%")) ||
                (p.PhoneNumber != null && EF.Functions.ILike(p.PhoneNumber, $"%{term}%")));
        }

        var patients = await query.OrderByDescending(p => p.CreatedAt).Take(500).ToListAsync();
        return patients.Select(ToDto).ToList();
    }

    [HttpGet("{id}")]
    [Authorize(Policy = nameof(Permission.ViewPatients))]
    public async Task<ActionResult<PatientDto>> GetById(int id)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return patient is null ? NotFound() : ToDto(patient);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManagePatients))]
    public async Task<ActionResult<PatientDto>> Create(PatientUpsertRequest request)
    {
        var patient = new Patient
        {
            FullName = request.FullName,
            DateOfBirth = request.DateOfBirth,
            Sex = request.Sex,
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        patient.PatientCode = $"P-{patient.Id:D5}";
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "PatientCreated", nameof(Patient), patient.Id.ToString(), patient.FullName);
        return CreatedAtAction(nameof(GetById), new { id = patient.Id }, ToDto(patient));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = nameof(Permission.ManagePatients))]
    public async Task<ActionResult<PatientDto>> Update(int id, PatientUpsertRequest request)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id);
        if (patient is null) return NotFound();

        db.Entry(patient).Property(p => p.RowVersion).OriginalValue = request.RowVersion;

        patient.FullName = request.FullName;
        patient.DateOfBirth = request.DateOfBirth;
        patient.Sex = request.Sex;
        patient.PhoneNumber = request.PhoneNumber;
        patient.Address = request.Address;
        patient.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This patient was changed by someone else. Reload and try again." });
        }

        await audit.LogAsync(User.GetUserId(), "PatientUpdated", nameof(Patient), patient.Id.ToString(), patient.FullName);
        return ToDto(patient);
    }

    private static PatientDto ToDto(Patient p) => new()
    {
        Id = p.Id,
        PatientCode = p.PatientCode,
        FullName = p.FullName,
        DateOfBirth = p.DateOfBirth,
        Sex = p.Sex,
        PhoneNumber = p.PhoneNumber,
        Address = p.Address,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        RowVersion = p.RowVersion,
    };
}
