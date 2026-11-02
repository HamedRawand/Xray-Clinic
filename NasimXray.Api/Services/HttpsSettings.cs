namespace NasimXray.Api.Services;

public class HttpsSettings
{
    public const string SectionName = "Https";

    public bool Enabled { get; set; }
    public int Port { get; set; } = 5443;
    public string PfxPath { get; set; } = string.Empty;
    public string PfxPassword { get; set; } = string.Empty;
}
