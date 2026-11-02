using NasimXray.Shared.Dtos.Auth;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Services;

public static class SessionService
{
    public static LoginResponse? Current { get; private set; }

    public static void Start(LoginResponse login) => Current = login;

    public static void End() => Current = null;

    // Called after a silent token refresh — keeps SessionService.Current's tokens in sync with
    // ApiClient's, since RestoreConfirmDialog/ChangePasswordDialog etc. don't hold their own copy.
    public static void UpdateTokens(LoginResponse refreshed)
    {
        if (Current is null) return;
        Current.AccessToken = refreshed.AccessToken;
        Current.RefreshToken = refreshed.RefreshToken;
        Current.AccessTokenExpiresAt = refreshed.AccessTokenExpiresAt;
    }

    public static bool Can(Permission permission) =>
        Current?.Permissions.Contains(permission) ?? false;
}
