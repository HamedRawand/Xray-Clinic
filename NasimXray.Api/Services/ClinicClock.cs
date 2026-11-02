using Microsoft.Extensions.Options;

namespace NasimXray.Api.Services;

public class ClinicSettings
{
    public const string SectionName = "Clinic";

    // IANA ("Asia/Kabul") or Windows ("Afghanistan Standard Time") id. Falls back to a fixed
    // UTC+4:30 offset — Afghanistan has no daylight saving — if the id can't be resolved.
    public string TimeZoneId { get; set; } = "Asia/Kabul";
}

// Timestamps are stored in UTC; "today" and date ranges must be evaluated in the clinic's own
// time zone, otherwise an exam done at 9pm Kabul time (16:30 UTC) lands on the wrong day.
public class ClinicClock
{
    private readonly TimeZoneInfo _zone;

    public ClinicClock(IOptions<ClinicSettings> options, ILogger<ClinicClock> logger)
    {
        var id = options.Value.TimeZoneId;
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Clinic time zone '{Id}' not found — using fixed UTC+04:30.", id);
            _zone = TimeZoneInfo.CreateCustomTimeZone("Clinic", TimeSpan.FromMinutes(270), "Clinic", "Clinic");
        }
    }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone));

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    // Start of the given clinic-local day, as a UTC instant.
    public DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), _zone);
}
