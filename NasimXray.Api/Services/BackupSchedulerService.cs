using Microsoft.Extensions.Options;

namespace NasimXray.Api.Services;

// Runs a nightly encrypted backup automatically, in addition to the admin's manual "Create
// Backup" button — see ARCHITECTURE_PLAN.md §8 ("recommended in addition to the manual admin
// button, since a multi-PC clinic app is a bigger loss if data is lost than a single desktop").
public class BackupSchedulerService(IServiceScopeFactory scopeFactory, IOptions<BackupSettings> settingsOptions, ILogger<BackupSchedulerService> logger) : BackgroundService
{
    private readonly BackupSettings _settings = settingsOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            logger.LogInformation("Next scheduled backup in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<BackupService>();
                await backupService.CreateBackupAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled backup failed");
            }
        }
    }

    private TimeSpan TimeUntilNextRun()
    {
        var now = DateTime.UtcNow;
        var next = new DateTime(now.Year, now.Month, now.Day, _settings.ScheduledHourUtc, 0, 0, DateTimeKind.Utc);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
