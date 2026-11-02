using System.Windows;
using System.Windows.Media;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class ServerSettingsWindow : Window
{
    private readonly string _originalUrl;
    private string? _trustedThumbprint;
    private string? _pendingThumbprint;

    public string? SavedServerUrl { get; private set; }
    public string? SavedCertThumbprint { get; private set; }

    public ServerSettingsWindow(string currentServerUrl, string? currentCertThumbprint = null)
    {
        InitializeComponent();
        ServerUrlBox.Text = currentServerUrl;
        _originalUrl = currentServerUrl;
        _trustedThumbprint = currentCertThumbprint;
        UpdateTrustedIndicator();
    }

    private void ServerUrlBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Changing the address invalidates any pin from a previous server — never carry a trust
        // decision for one server over to a different address.
        if (ServerUrlBox.Text.Trim() != _originalUrl)
        {
            _trustedThumbprint = null;
            _pendingThumbprint = null;
        }
        CertPanel.Visibility = Visibility.Collapsed;
        UpdateTrustedIndicator();
    }

    private bool IsHttps => ServerUrlBox.Text.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus((string)FindResource("ServerSettings.EnterAddressFirst"), isError: true);
            return;
        }

        SetStatus((string)FindResource("ServerSettings.Testing"), isError: false);

        if (IsHttps && string.IsNullOrEmpty(_trustedThumbprint) && string.IsNullOrEmpty(_pendingThumbprint))
        {
            // Nothing pinned yet for this address — probe and show the certificate for the
            // admin to explicitly trust before any real connection is attempted with it.
            var thumbprint = await ApiClient.ProbeServerCertificateThumbprintAsync(NormalizeUrl(url));
            if (thumbprint is null)
            {
                SetStatus((string)FindResource("ServerSettings.ConnectFailed"), isError: true);
                return;
            }

            _pendingThumbprint = thumbprint;
            ThumbprintText.Text = FormatThumbprint(thumbprint);
            CertPanel.Visibility = Visibility.Visible;
            SetStatus(string.Empty, isError: false);
            return;
        }

        var client = new ApiClient(NormalizeUrl(url), _trustedThumbprint ?? _pendingThumbprint);
        var ok = await client.TestConnectionAsync();
        SetStatus((string)FindResource(ok ? "ServerSettings.Connected" : "ServerSettings.ConnectFailed"), isError: !ok);
    }

    private void Trust_Click(object sender, RoutedEventArgs e)
    {
        _trustedThumbprint = _pendingThumbprint;
        CertPanel.Visibility = Visibility.Collapsed;
        UpdateTrustedIndicator();
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
        SavedCertThumbprint = IsHttps ? (_trustedThumbprint ?? _pendingThumbprint) : null;
        DialogResult = true;
        Close();
    }

    private void UpdateTrustedIndicator()
    {
        if (IsHttps && !string.IsNullOrEmpty(_trustedThumbprint))
        {
            TrustedText.Text = $"{(string)FindResource("ServerSettings.Trusted")}{FormatThumbprint(_trustedThumbprint)}";
            TrustedText.Visibility = Visibility.Visible;
        }
        else
        {
            TrustedText.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatThumbprint(string thumbprint) =>
        string.Join(" ", Enumerable.Range(0, thumbprint.Length / 2).Select(i => thumbprint.Substring(i * 2, 2)));

    private static string NormalizeUrl(string url) => url.TrimEnd('/') + "/";

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("SuccessBrush");
    }
}
