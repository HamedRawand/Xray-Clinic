using System.ComponentModel.DataAnnotations;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Models;

public class User
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public int? CustomRoleId { get; set; }
    public CustomRole? CustomRole { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int FailedLoginAttempts { get; set; }
    public bool MustChangePassword { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string RoleDisplayName => CustomRole?.Name ?? Role.ToString();
}
