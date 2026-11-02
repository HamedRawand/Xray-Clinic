using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace NasimXray.Client.Services;

public class UpdateInfo
{
    public Version Version { get; set; } = new(0, 0, 0);
    public string TagName { get; set; } = string.Empty;
    public string? ReleaseNotes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
}

// Client auto-updater — same pattern as OPD Clinic's Phase 21 updater, adapted for this repo's
// two-tag-prefix convention (client-vX.Y.Z / server-vX.Y.Z) since GitHub's plain /releases/latest
// endpoint would return whichever tag (client or server) was published most recently, not
// necessarily a client release. See ARCHITECTURE_PLAN.md §9 and §11.
public static class UpdateService
{
    private const string Owner = "HamedRawand";
    private const string Repo = "Xray-Clinic";
    private const string TagPrefix = "client-v";
    private const int CooldownHours = 24;

    private static string CooldownFilePath => Path.Combine(
        NasimXray.Shared.AppFolders.Resolve(Environment.SpecialFolder.LocalApplicationData), "last_update_check.txt");

    public static async Task<UpdateInfo?> CheckForUpdateAsync(bool ignoreCooldown = false) =>
        (await CheckAsync(ignoreCooldown)).Update;

    // Same check, but tells "nothing newer" apart from "could not reach GitHub" (Failed) — the manual
    // "Check for updates" button needs to say which.
    public static async Task<(UpdateInfo? Update, bool Failed)> CheckAsync(bool ignoreCooldown)
    {
        if (!ignoreCooldown && !CooldownElapsed()) return (null, false);

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DiagnosticClinicManager-Updater");
            http.Timeout = TimeSpan.FromSeconds(10);

            var releases = await http.GetFromJsonAsync<List<GitHubRelease>>(
                $"https://api.github.com/repos/{Owner}/{Repo}/releases");

            WriteCooldownStamp();

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion is null) return (null, false);

            // Walk candidates newest-first and take the first one that's both newer than the
            // current install AND has an installer asset attached — a release published via
            // `gh release create` without its asset yet (the two-step upload OPD Clinic's memory
            // warns about) must not make the checker give up; it should fall back to the next
            // newest release that's actually downloadable.
            var candidate = releases?
                .Where(r => !r.Draft && !r.Prerelease && r.TagName.StartsWith(TagPrefix, StringComparison.Ordinal))
                .Select(r => (Release: r, Version: ParseVersion(r.TagName)))
                .Where(x => x.Version is not null && x.Version! > currentVersion)
                .OrderByDescending(x => x.Version)
                .Select(x => (x.Release, x.Version, Asset: x.Release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))))
                .FirstOrDefault(x => x.Asset is not null);

            if (candidate is null || candidate.Value.Asset is null) return (null, false);

            return (new UpdateInfo
            {
                Version = candidate.Value.Version!,
                TagName = candidate.Value.Release.TagName,
                ReleaseNotes = candidate.Value.Release.Body,
                DownloadUrl = candidate.Value.Asset.BrowserDownloadUrl,
            }, false);
        }
        catch
        {
            // GitHub unreachable / rate-limited / network hiccup should never block app startup.
            return (null, true);
        }
    }

    // The version of this installed copy, as shown on the sign-in screen (e.g. "1.1.2").
    public static string CurrentVersionText()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public static async Task<string> DownloadInstallerAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DiagnosticClinicManager-Updater");
        http.Timeout = TimeSpan.FromMinutes(10);

        var tempPath = Path.Combine(Path.GetTempPath(), $"DiagnosticClinicManager_Setup_{info.Version}.exe");

        using var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1L;

        await using var responseStream = await response.Content.ReadAsStreamAsync();

        // Explicit block scope so the file stream closes before Process.Start runs the installer
        // — OPD Clinic hit "file is being used by another process" from keeping this open at
        // method scope instead (see that project's Auto-Update Pitfalls memory).
        await using (var file = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await responseStream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                readTotal += read;
                if (total > 0) progress?.Report((double)readTotal / total);
            }
        }

        return tempPath;
    }

    public static void RunInstallerAndExit(string installerPath)
    {
        // Runs with the installer's normal UI (no /SILENT) since the user already explicitly
        // clicked "Update Now" — this sidesteps OPD Clinic's `skipifsilent` relaunch pitfall
        // entirely, since that only bites when the installer runs in /SILENT mode.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private static Version? ParseVersion(string tagName)
    {
        if (!tagName.StartsWith(TagPrefix, StringComparison.Ordinal)) return null;
        return Version.TryParse(tagName[TagPrefix.Length..], out var v) ? v : null;
    }

    private static bool CooldownElapsed()
    {
        if (!File.Exists(CooldownFilePath)) return true;
        if (!DateTime.TryParse(File.ReadAllText(CooldownFilePath), out var last)) return true;
        return DateTime.UtcNow - last > TimeSpan.FromHours(CooldownHours);
    }

    private static void WriteCooldownStamp()
    {
        var dir = Path.GetDirectoryName(CooldownFilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(CooldownFilePath, DateTime.UtcNow.ToString("O"));
    }
}
