using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.Users;

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int? CustomRoleId { get; set; }
    public string? CustomRoleName { get; set; }
    public string RoleDisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserCreateRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int? CustomRoleId { get; set; }
}

public class UserUpdateRequest
{
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int? CustomRoleId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
