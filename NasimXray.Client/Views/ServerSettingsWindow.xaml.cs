using System.Windows;
using System.Windows.Media;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class ServerSettingsWindow : Window
{
    public string? SavedServerUrl { get; private set; }

    public ServerSettingsWindow(string currentServerUrl)
    {
        InitializeComponent();
        ServerUrlBox.Text = currentServerUrl;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus((string)FindResource("ServerSettings.EnterAddressFirst"), isError: true);
            return;
        }

        SetStatus((string)FindResource("ServerSettings.Testing"), isError: false);
        var client = new ApiClient(NormalizeUrl(url));
        var ok = await client.TestConnectionAsync();
        SetStatus((string)FindResource(ok ? "ServerSettings.Connected" : "ServerSettings.ConnectFailed"), isError: !ok);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus((string)FindResource("ServerSettings.EnterAddressFirst"), isError: true);
            return;
        }

        SavedServerUrl = NormalizeUrl(url);
        DialogResult = true;
        Close();
    }

    private static string NormalizeUrl(string url) => url.TrimEnd('/') + "/";

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("SuccessBrush");
    }
}
