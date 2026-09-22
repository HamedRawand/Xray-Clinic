using System.ComponentModel.DataAnnotations;

namespace NasimXray.Api.Models;

public class ReferringPhysician
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ClinicName { get; set; }

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;
}
