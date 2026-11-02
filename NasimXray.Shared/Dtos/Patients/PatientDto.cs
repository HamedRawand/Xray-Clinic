namespace NasimXray.Shared.Dtos.Patients;

public class PatientDto
{
    public int Id { get; set; }
    public string? PatientCode { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Sex { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    // Exams booked for this patient, cancelled ones excluded. Filled in by the list and detail endpoints.
    public int ExamCount { get; set; }
    // When the most recent (non-cancelled) exam was booked; null if the patient has none yet.
    public DateTime? LastVisitAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public uint RowVersion { get; set; }
}

public class PatientUpsertRequest
{
    public string FullName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Sex { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public uint RowVersion { get; set; }
}
