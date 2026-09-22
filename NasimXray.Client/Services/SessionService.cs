using NasimXray.Shared.Dtos.Auth;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Services;

public static class SessionService
{
    public static LoginResponse? Current { get; private set; }

    public static void Start(LoginResponse login) => Current = login;

    public static void End() => Current = null;

    public static bool Can(Permission permission) =>
        Current?.Permissions.Contains(permission) ?? false;
}
