using System.Diagnostics;
using System.IO;
using System.Windows;

namespace NasimXray.Client.Services;

// Opens the PDF installation and configuration guide that is installed next to the program (Manual\ folder).
public static class ManualService
{
    private const string FileName = "DiagnosticClinicManager_Manual_EN.pdf";

    public static string ManualPath => Path.Combine(AppContext.BaseDirectory, "Manual", FileName);

    public static void Open(Window? owner)
    {
        var path = ManualPath;
        string Text(string key) => (string)Application.Current.FindResource(key);

        if (!File.Exists(path))
        {
            Show(owner, string.Format(Text("Help.ManualMissing"), path));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            Show(owner, string.Format(Text("Help.ManualNoViewer"), path));
        }
    }

    private static void Show(Window? owner, string message)
    {
        if (owner is not null) MessageBox.Show(owner, message, ClinicInfoService.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(message, ClinicInfoService.Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
