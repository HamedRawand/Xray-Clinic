using System.ComponentModel.DataAnnotations;

namespace NasimXray.Api.Models;

// Exactly one row (Id = 1): the identity of the clinic this installation serves.
public class ClinicProfile
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string NameEnglish { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? NameDari { get; set; }

    [MaxLength(200)]
    public string? Tagline { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    // Optional letterhead logo (PNG or JPEG, small — validated on upload).
    public byte[]? LogoData { get; set; }

    [MaxLength(30)]
    public string? LogoContentType { get; set; }

    public bool IsConfigured { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public uint RowVersion { get; set; }
}
