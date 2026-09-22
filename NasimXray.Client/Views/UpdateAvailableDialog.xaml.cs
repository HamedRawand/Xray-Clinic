using System.Windows;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class UpdateAvailableDialog : Window
{
    private readonly UpdateInfo _info;

    public UpdateAvailableDialog(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;

        NewVersionRun.Text = $"v{info.Version}";
        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionRun.Text = $"v{currentVersion}";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "—" : info.ReleaseNotes;
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        LaterButton.IsEnabled = false;
        UpdateNowButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;

        try
        {
            var progress = new Progress<double>(p => DownloadProgress.Value = p);
            var installerPath = await UpdateService.DownloadInstallerAsync(_info, progress);
            UpdateService.RunInstallerAndExit(installerPath);
        }
        catch (Exception)
        {
            ErrorText.Text = (string)FindResource("Update.DownloadFailed");
            ErrorText.Visibility = Visibility.Visible;
            LaterButton.IsEnabled = true;
            UpdateNowButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }
}
