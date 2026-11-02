namespace NasimXray.Shared.Dtos.Clinic;

// Who this installation belongs to. One clinic per installation: the name, contact details and logo
// appear in the window title, login screen, message boxes and on every printed report and receipt.
public class ClinicProfileDto
{
    public string NameEnglish { get; set; } = string.Empty;
    public string? NameDari { get; set; }
    public string? Tagline { get; set; }      // line under the name on printouts, e.g. the services offered
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool HasLogo { get; set; }
    // Changes whenever the profile or logo is saved — clients re-download the logo only when it differs.
    public long Version { get; set; }
    // False until an admin has saved the profile once (drives the "set up your clinic" prompt).
    public bool IsConfigured { get; set; }
    public uint RowVersion { get; set; }
}

public class ClinicProfileUpdateRequest
{
    public string NameEnglish { get; set; } = string.Empty;
    public string? NameDari { get; set; }
    public string? Tagline { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public uint RowVersion { get; set; }
}
