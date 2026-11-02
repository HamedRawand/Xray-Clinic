using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos;
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
    public async Task<ActionResult<PagedResult<PatientDto>>> GetAll(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize)
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

        var total = await query.CountAsync();
        var (currentPage, size) = Paging.Normalize(page, pageSize, total);

        var patients = await query
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((currentPage - 1) * size).Take(size)
            .ToListAsync();

        // One grouped query for the whole page (not one per patient).
        var ids = patients.Select(p => p.Id).ToList();
        var counts = await db.ExamOrders.AsNoTracking()
            .Where(o => ids.Contains(o.PatientId) && o.Status != ExamOrderStatus.Cancelled)
            .GroupBy(o => o.PatientId)
            .Select(g => new { PatientId = g.Key, Count = g.Count(), Last = g.Max(o => o.CreatedAt) })
            .ToDictionaryAsync(x => x.PatientId);

        return new PagedResult<PatientDto>
        {
            Items = patients.Select(p =>
            {
                var dto = ToDto(p);
                if (counts.TryGetValue(p.Id, out var stats))
                {
                    dto.ExamCount = stats.Count;
                    dto.LastVisitAt = stats.Last;
                }
                return dto;
            }).ToList(),
            TotalCount = total,
            Page = currentPage,
            PageSize = size,
        };
    }

    // Soft check used by the check-in wizard before registering someone new: same phone number, or the
    // name (case-insensitive; with 2+ words, all words in any order). The clinic decides whether it is really
    // the same person.
    [HttpGet("possible-duplicates")]
    [Authorize(Policy = nameof(Permission.ViewPatients))]
    public async Task<ActionResult<List<PatientDto>>> PossibleDuplicates([FromQuery] string? fullName, [FromQuery] string? phoneNumber)
    {
        var name = fullName?.Trim();
        var phone = phoneNumber?.Trim();
        var hasName = !string.IsNullOrEmpty(name) && name.Length >= 2;
        var hasPhone = !string.IsNullOrEmpty(phone) && phone.Length >= 5;
        if (!hasName && !hasPhone) return new List<PatientDto>();

        var matches = new Dictionary<int, Patient>();

        if (hasName)
        {
            // Words of the typed name (wildcard characters stripped so they can't widen the match).
            var words = name!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.Replace("%", "").Replace("_", ""))
                .Where(w => w.Length >= 2)
                .ToList();

            IQueryable<Patient> byName = db.Patients.AsNoTracking();
            if (words.Count >= 2)
            {
                // Two or more words: every word must appear, in any order ("Karimi Ahmad" finds "Ahmad Karimi").
                foreach (var word in words)
                {
                    var pattern = $"%{word}%";
                    byName = byName.Where(p => EF.Functions.ILike(p.FullName, pattern));
                }
            }
            else if (words.Count == 1)
            {
                // A single word would match far too many people on "contains"; require the same name.
                var exact = words[0];
                byName = byName.Where(p => EF.Functions.ILike(p.FullName, exact));
            }
            else
            {
                byName = byName.Where(p => false);
            }

            foreach (var p in await byName.OrderByDescending(p => p.CreatedAt).Take(5).ToListAsync())
                matches[p.Id] = p;
        }

        if (hasPhone)
        {
            foreach (var p in await db.Patients.AsNoTracking().Where(p => p.PhoneNumber == phone)
                         .OrderByDescending(p => p.CreatedAt).Take(5).ToListAsync())
                matches[p.Id] = p;
        }

        return matches.Values.OrderByDescending(p => p.CreatedAt).Take(5).Select(ToDto).ToList();
    }

    [HttpGet("{id}")]
    [Authorize(Policy = nameof(Permission.ViewPatients))]
    public async Task<ActionResult<PatientDto>> GetById(int id)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (patient is null) return NotFound();

        var dto = ToDto(patient);
        var exams = db.ExamOrders.Where(o => o.PatientId == id && o.Status != ExamOrderStatus.Cancelled);
        dto.ExamCount = await exams.CountAsync();
        dto.LastVisitAt = await exams.MaxAsync(o => (DateTime?)o.CreatedAt);
        return dto;
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

    internal static PatientDto ToDto(Patient p) => new()
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
