using System.ComponentModel.DataAnnotations;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Models;

public class CustomRole
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }

    // Comma-separated Permission enum names — same pattern as OPD Clinic's CustomRole.
    public string PermissionsJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public HashSet<Permission> GetPermissions() =>
        PermissionsJson
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Enum.TryParse<Permission>(p.Trim(), out var perm) ? perm : (Permission?)null)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToHashSet();

    public void SetPermissions(IEnumerable<Permission> permissions) =>
        PermissionsJson = string.Join(',', permissions.Select(p => p.ToString()));
}
