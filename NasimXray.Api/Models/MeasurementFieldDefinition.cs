using System.ComponentModel.DataAnnotations;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Models;

public class MeasurementFieldDefinition
{
    public int Id { get; set; }

    public int ExamTypeId { get; set; }
    public ExamType ExamType { get; set; } = null!;

    [MaxLength(50)]
    public string FieldKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Unit { get; set; }

    public MeasurementDataType DataType { get; set; } = MeasurementDataType.Number;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
