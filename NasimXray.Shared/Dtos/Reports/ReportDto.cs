using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.Reports;

public class MeasurementValueDto
{
    public int FieldDefinitionId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public MeasurementDataType DataType { get; set; }
    public string? Value { get; set; }
}

public class ReportAmendmentDto
{
    public int Id { get; set; }
    public string? PreviousFindings { get; set; }
    public string? PreviousImpression { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string AmendedByName { get; set; } = string.Empty;
    public DateTime AmendedAt { get; set; }
}

public class ReportDto
{
    public int Id { get; set; }
    public int ExamOrderId { get; set; }
    public string? Findings { get; set; }
    public string? Impression { get; set; }
    public int ReportedById { get; set; }
    public string ReportedByName { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string? FinalizedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public uint RowVersion { get; set; }
    public List<MeasurementValueDto> Measurements { get; set; } = [];
    public List<ReportAmendmentDto> Amendments { get; set; } = [];
}

public class MeasurementValueRequest
{
    public int FieldDefinitionId { get; set; }
    public string? Value { get; set; }
}

public class ReportSaveRequest
{
    public string? Findings { get; set; }
    public string? Impression { get; set; }
    public List<MeasurementValueRequest> Measurements { get; set; } = [];
    public uint? RowVersion { get; set; }
}

public class ReportFinalizeRequest
{
    public uint RowVersion { get; set; }
}

public class ReportAmendRequest
{
    public string? Findings { get; set; }
    public string? Impression { get; set; }
    public List<MeasurementValueRequest> Measurements { get; set; } = [];
    public string Reason { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
}
