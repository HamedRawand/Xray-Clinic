using System.IO;
using System.Text.Json;

namespace NasimXray.Client.Services;

public class ClientSettings
{
    public string ServerBaseUrl { get; set; } = string.Empty;
    public string Language { get; set; } = "English";
    public string CalendarType { get; set; } = "Gregorian";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NasimXrayClinic", "client_settings.json");

    public static ClientSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new ClientSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ClientSettings>(json) ?? new ClientSettings();
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
