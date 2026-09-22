using NasimXray.Shared.Enums;

namespace NasimXray.Shared.Dtos.Auth;

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }

    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string RoleDisplayName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public List<Permission> Permissions { get; set; } = [];
}
