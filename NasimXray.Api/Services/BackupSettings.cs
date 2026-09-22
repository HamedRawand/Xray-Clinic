namespace NasimXray.Api.Services;

public class BackupSettings
{
    public const string SectionName = "Backup";

    public string Folder { get; set; } = string.Empty;
    public string EncryptionPassword { get; set; } = string.Empty;
    public string PostgresBinDirectory { get; set; } = @"C:\Program Files\PostgreSQL\17\bin";
    public int RetentionCount { get; set; } = 14;
    public int ScheduledHourUtc { get; set; } = 22; // ~2 AM local time in Afghanistan (UTC+4:30)
}
