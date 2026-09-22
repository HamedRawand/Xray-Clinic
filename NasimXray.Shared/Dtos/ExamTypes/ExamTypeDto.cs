using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.ExamTypes;

public class MeasurementFieldDto
{
    public int Id { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public MeasurementDataType DataType { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MeasurementFieldUpsertRequest
{
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public MeasurementDataType DataType { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ExamTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Modality { get; set; }
    public string? BodyPart { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public List<MeasurementFieldDto> MeasurementFields { get; set; } = [];
}

public class ExamTypeUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Modality { get; set; }
    public string? BodyPart { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
