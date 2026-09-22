using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NasimXray.Api.Services;
using NasimXray.Shared.Dtos.Backup;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Controllers;

[ApiController]
[Route("api/backup")]
[Authorize(Policy = nameof(Permission.ManageBackup))]
public class BackupController(BackupService backupService, AuditService audit) : ControllerBase
{
    [HttpGet]
    public ActionResult<List<BackupFileDto>> GetAll() =>
        backupService.ListBackups()
            .Select(b => new BackupFileDto { FileName = b.FileName, SizeBytes = b.SizeBytes, CreatedAtUtc = b.CreatedAtUtc })
            .ToList();

    [HttpPost("create")]
    public async Task<ActionResult<BackupFileDto>> Create()
    {
        try
        {
            var backup = await backupService.CreateBackupAsync();
            await audit.LogAsync(User.GetUserId(), "BackupCreated", "Backup", backup.FileName, $"{backup.SizeBytes} bytes");
            return new BackupFileDto { FileName = backup.FileName, SizeBytes = backup.SizeBytes, CreatedAtUtc = backup.CreatedAtUtc };
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore(RestoreBackupRequest request)
    {
        try
        {
            await backupService.RestoreBackupAsync(request.FileName, request.Password);
            await audit.LogAsync(User.GetUserId(), "DatabaseRestored", "Backup", request.FileName);
            return NoContent();
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "Backup file not found." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
