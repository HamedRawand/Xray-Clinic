using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.Roles;

public class CustomRoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Permission> Permissions { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public class CustomRoleUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Permission> Permissions { get; set; } = [];
}
