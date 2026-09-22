using System.ComponentModel.DataAnnotations;

namespace NasimXray.Api.Models;

public class Patient
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string? PatientCode { get; set; }

    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(10)]
    public string? Sex { get; set; }

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Optimistic concurrency — Npgsql maps this to Postgres' xmin system column.
    public uint RowVersion { get; set; }
}
