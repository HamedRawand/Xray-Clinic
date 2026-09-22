namespace NasimXray.Shared.Dtos.ReferringPhysicians;

public class ReferringPhysicianDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ClinicName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ReferringPhysicianUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ClinicName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
}
