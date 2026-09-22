namespace NasimXray.Api.Models;

public class ReportMeasurement
{
    public int Id { get; set; }

    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public int FieldDefinitionId { get; set; }
    public MeasurementFieldDefinition FieldDefinition { get; set; } = null!;

    public string? Value { get; set; }
}
