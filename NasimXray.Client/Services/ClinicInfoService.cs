using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NasimXray.Client.Helpers;
using NasimXray.Shared.Dtos.Clinic;
using Serilog;

namespace NasimXray.Client.Services;

// The clinic's identity (name, contact details, logo) as the rest of the client sees it.
//  • Loaded from the server (public endpoint, so the login screen can show it before sign-in) and cached on disk,
//    so the right name is shown even when the server is briefly unreachable.
//  • Drives the "App.Title" resource (window titles, headers, message-box titles) and the letterhead of printouts.
//  • Follows the UI language: English UI shows the English name, Dari UI the Dari name (falling back to the other).
public static class ClinicInfoService
{
    private const string FallbackTitle = "Diagnostic Clinic Manager";

    private static readonly string Dir = NasimXray.Shared.AppFolders.Resolve(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ProfilePath = Path.Combine(Dir, "clinic_profile.json");
    private static readonly string LogoPath = Path.Combine(Dir, "clinic_logo.bin");

    private static bool _hooked;

    public static ClinicProfileDto? Profile { get; private set; }
    public static byte[]? LogoBytes { get; private set; }
    public static ImageSource? Logo { get; private set; }

    // Raised (on the UI thread) whenever the name, details or logo changed.
    public static event Action? Changed;

    public static string Title
    {
        get
        {
            var english = Profile?.NameEnglish;
            var dari = Profile?.NameDari;
            var preferred = AppSession.Language == AppLanguage.Dari ? dari : english;
            var other = AppSession.Language == AppLanguage.Dari ? english : dari;
            if (!string.IsNullOrWhiteSpace(preferred)) return preferred!;
            if (!string.IsNullOrWhiteSpace(other)) return other!;
            return FallbackTitle;
        }
    }

    // Unknown (never loaded) counts as configured so nobody is nagged because of a connection problem.
    public static bool IsConfigured => Profile?.IsConfigured ?? true;

    // Call once at startup, before the first window is created.
    public static void LoadCache()
    {
        try
        {
            if (File.Exists(ProfilePath))
                Profile = JsonSerializer.Deserialize<ClinicProfileDto>(File.ReadAllText(ProfilePath));
            if (File.Exists(LogoPath))
                LogoBytes = File.ReadAllBytes(LogoPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the cached clinic profile");
        }

        Hook();
        Apply();
    }

    // Anonymous refresh (login screen): no session yet, just the server address.
    public static async Task RefreshAsync(string baseUrl, string? certificateThumbprint)
    {
        try
        {
            await RefreshAsync(new ApiClient(baseUrl, certificateThumbprint));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Clinic profile refresh (anonymous) failed");
        }
    }

    public static async Task RefreshAsync(ApiClient api)
    {
        try
        {
            var profile = await api.GetClinicProfileAsync();

            var logo = LogoBytes;
            if (!profile.HasLogo) logo = null;
            else if (Profile is null || Profile.Version != profile.Version || logo is null)
                logo = await api.GetClinicLogoAsync();

            Profile = profile;
            LogoBytes = logo;
            SaveCache();
            Apply();
        }
        catch (Exception ex)
        {
            // Keep whatever is cached; the next refresh will try again.
            Log.Debug(ex, "Clinic profile refresh failed");
        }
    }

    public static ImageSource? ToImage(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        AppSession.LanguageChanged += Apply;
    }

    private static void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ProfilePath, JsonSerializer.Serialize(Profile));
            if (LogoBytes is { Length: > 0 }) File.WriteAllBytes(LogoPath, LogoBytes);
            else if (File.Exists(LogoPath)) File.Delete(LogoPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not cache the clinic profile");
        }
    }

    private static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Apply);
            return;
        }

        Logo = ToImage(LogoBytes);
        // Replaces the "App.Title" resource at application level, so every {DynamicResource App.Title} updates live.
        app.Resources["App.Title"] = Title;
        Changed?.Invoke();
    }
}
