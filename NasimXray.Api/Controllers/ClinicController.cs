using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Clinic;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

// The clinic's identity. Reading it is public (the login screen shows the name before anyone signs in; none
// of it is secret), changing it needs ManageClinicSettings.
[ApiController]
[Route("api")]
public class ClinicController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const int MaxLogoBytes = 300 * 1024;

    [HttpGet("system/clinic")]
    [AllowAnonymous]
    public async Task<ActionResult<ClinicProfileDto>> Get() => ToDto(await LoadAsync(tracking: false));

    [HttpGet("system/clinic/logo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLogo()
    {
        var profile = await LoadAsync(tracking: false);
        if (profile.LogoData is not { Length: > 0 } || string.IsNullOrEmpty(profile.LogoContentType)) return NotFound();
        return File(profile.LogoData, profile.LogoContentType);
    }

    [HttpPut("clinic")]
    [Authorize(Policy = nameof(Permission.ManageClinicSettings))]
    public async Task<ActionResult<ClinicProfileDto>> Update(ClinicProfileUpdateRequest request)
    {
        var name = request.NameEnglish?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { message = "The clinic name (English) is required." });
        if (name.Length > 150 || (request.NameDari?.Trim().Length ?? 0) > 150)
            return BadRequest(new { message = "The clinic name is too long (150 characters max)." });
        if ((request.Tagline?.Trim().Length ?? 0) > 200 || (request.Address?.Trim().Length ?? 0) > 250 ||
            (request.Phone?.Trim().Length ?? 0) > 100 || (request.Email?.Trim().Length ?? 0) > 100)
            return BadRequest(new { message = "One of the fields is too long." });

        var profile = await LoadAsync(tracking: true);
        db.Entry(profile).Property(p => p.RowVersion).OriginalValue = request.RowVersion;

        profile.NameEnglish = name;
        profile.NameDari = Clean(request.NameDari);
        profile.Tagline = Clean(request.Tagline);
        profile.Address = Clean(request.Address);
        profile.Phone = Clean(request.Phone);
        profile.Email = Clean(request.Email);
        profile.IsConfigured = true;
        profile.UpdatedAt = DateTime.UtcNow;

        if (!await TrySaveAsync()) return Conflict(new { message = "The clinic settings were changed by someone else. Reload and try again." });

        await audit.LogAsync(User.GetUserId(), "ClinicProfileUpdated", nameof(ClinicProfile), profile.Id.ToString(),
            $"{profile.NameEnglish} / {profile.NameDari}");
        return ToDto(profile);
    }

    // Raw image bytes in the request body (Content-Type is ignored — the file's own signature decides).
    [HttpPut("clinic/logo")]
    [Authorize(Policy = nameof(Permission.ManageClinicSettings))]
    [RequestSizeLimit(MaxLogoBytes + 1024)]
    public async Task<ActionResult<ClinicProfileDto>> UploadLogo()
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        if (bytes.Length == 0) return BadRequest(new { message = "No image was sent." });
        if (bytes.Length > MaxLogoBytes) return BadRequest(new { message = "The logo is too large (300 KB max)." });

        var contentType = DetectImageType(bytes);
        if (contentType is null) return BadRequest(new { message = "The logo must be a PNG or JPEG image." });

        var profile = await LoadAsync(tracking: true);
        profile.LogoData = bytes;
        profile.LogoContentType = contentType;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "ClinicLogoUpdated", nameof(ClinicProfile), profile.Id.ToString(), $"{bytes.Length} bytes");
        return ToDto(profile);
    }

    [HttpDelete("clinic/logo")]
    [Authorize(Policy = nameof(Permission.ManageClinicSettings))]
    public async Task<ActionResult<ClinicProfileDto>> RemoveLogo()
    {
        var profile = await LoadAsync(tracking: true);
        profile.LogoData = null;
        profile.LogoContentType = null;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "ClinicLogoRemoved", nameof(ClinicProfile), profile.Id.ToString());
        return ToDto(profile);
    }

    // The single profile row; created on demand if the seeder has somehow not run.
    private async Task<ClinicProfile> LoadAsync(bool tracking)
    {
        var query = tracking ? db.ClinicProfiles : db.ClinicProfiles.AsNoTracking();
        var profile = await query.OrderBy(p => p.Id).FirstOrDefaultAsync();
        if (profile is not null) return profile;

        profile = new ClinicProfile { Id = 1, NameEnglish = "My Clinic" };
        db.ClinicProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // PNG: 89 50 4E 47 0D 0A 1A 0A · JPEG: FF D8 FF
    private static string? DetectImageType(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A ? "image/png" :
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg" :
        null;

    private static ClinicProfileDto ToDto(ClinicProfile p) => new()
    {
        NameEnglish = p.NameEnglish,
        NameDari = p.NameDari,
        Tagline = p.Tagline,
        Address = p.Address,
        Phone = p.Phone,
        Email = p.Email,
        HasLogo = p.LogoData is { Length: > 0 },
        Version = p.UpdatedAt.Ticks,
        IsConfigured = p.IsConfigured,
        RowVersion = p.RowVersion,
    };
}
