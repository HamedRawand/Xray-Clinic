namespace NasimXray.Shared.Dtos.Backup;

public class BackupFileDto
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class RestoreBackupRequest
{
    public string FileName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
