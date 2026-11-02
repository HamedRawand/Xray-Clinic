using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NasimXray.Api.Data;
using NasimXray.Api.Models;
using NasimXray.Shared.Dtos.Auth;

namespace NasimXray.Api.Services;

public class AuthService(AppDbContext db, IOptions<JwtSettings> jwtOptions)
{
    private const int MaxFailedAttempts = 5;
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<LoginResponse?> LoginAsync(string username, string password)
    {
        var user = await db.Users
            .Include(u => u.CustomRole)
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user is null || !user.IsActive)
            return null;

        if (user.IsLocked)
            throw new InvalidOperationException("Account is locked. Contact an administrator.");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
                user.IsLocked = true;
            await db.SaveChangesAsync();
            return null;
        }

        user.FailedLoginAttempts = 0;
        await db.SaveChangesAsync();

        return await IssueTokensAsync(user);
    }

    // Used by the client's idle-lock unlock screen — verifies identity without issuing new
    // tokens or touching MustChangePassword/lockout state (unlike login, this never changes the
    // existing session, it only confirms "yes, this is still the same person").
    public async Task<bool> VerifyPasswordAsync(int userId, string password)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return user is not null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task AdminResetPasswordAsync(int userId, string newPassword)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.MustChangePassword = true;
        user.IsLocked = false;
        user.FailedLoginAttempts = 0;
        await db.SaveChangesAsync();
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.CustomRole)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (stored is null || !stored.IsActive)
            return null;

        stored.RevokedAt = DateTime.UtcNow;
        return await IssueTokensAsync(stored.User);
    }

    private async Task<LoginResponse> IssueTokensAsync(User user)
    {
        var permissions = PermissionService.GetPermissions(user).ToList();

        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var accessToken = CreateAccessToken(user, permissions, accessTokenExpiresAt);

        var refreshTokenPlain = GenerateRefreshTokenValue();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(refreshTokenPlain),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays),
        });
        await db.SaveChangesAsync();

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenPlain,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            UserId = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Role = user.Role,
            RoleDisplayName = user.RoleDisplayName,
            MustChangePassword = user.MustChangePassword,
            Permissions = permissions,
        };
    }

    private string CreateAccessToken(User user, List<Shared.Enums.Permission> permissions, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim("permission", p.ToString())));

        var key = new SymmetricSecurityKey(Convert.FromBase64String(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshTokenValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
