using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NasimXray.Api.Models;

public class ExamType
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Modality { get; set; }

    [MaxLength(50)]
    public string? BodyPart { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public List<MeasurementFieldDefinition> MeasurementFields { get; set; } = [];
}
