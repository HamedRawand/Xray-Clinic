using System.IO;
using System.Text.Json;

namespace NasimXray.Client.Services;

public class ClientSettings
{
    public string ServerBaseUrl { get; set; } = string.Empty;
    public string Language { get; set; } = "English";
    public string CalendarType { get; set; } = "Gregorian";

    // Trust-on-first-use pin for the server's self-signed HTTPS certificate — set once via
    // ServerSettingsWindow's "Trust this certificate" step, then every connection is validated
    // against this specific thumbprint instead of either (a) blind ServicePointManager bypass
    // (accepts ANY server's self-signed cert — a real MITM risk) or (b) requiring every clinic
    // PC to have the cert manually imported into its Windows trust store (an extra deployment
    // step this avoids). Null/empty means HTTPS to this server has not been trusted yet.
    public string? ServerCertificateThumbprint { get; set; }

    private static readonly string SettingsPath = Path.Combine(
        NasimXray.Shared.AppFolders.Resolve(Environment.SpecialFolder.LocalApplicationData), "client_settings.json");

    public static ClientSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new ClientSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<ClientSettings>(json) ?? new ClientSettings();
            // Self-heal an address saved with a pasted path (https://host:5443/api/system/clinic/): every request is made
            // relative to it, so only scheme + host + port may remain.
            if (!string.IsNullOrWhiteSpace(settings.ServerBaseUrl))
                settings.ServerBaseUrl = Helpers.ServerAddress.Normalize(settings.ServerBaseUrl) ?? settings.ServerBaseUrl;
            return settings;
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }
}
