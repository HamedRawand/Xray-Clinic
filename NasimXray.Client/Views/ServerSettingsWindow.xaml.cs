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
        // Compare cleaned-up forms, so that merely tidying the typed address (adding "/" or dropping a pasted path)
        // does not throw away a trust decision made for the same server.
        var typed = Helpers.ServerAddress.Normalize(ServerUrlBox.Text) ?? ServerUrlBox.Text.Trim();
        var original = Helpers.ServerAddress.Normalize(_originalUrl) ?? _originalUrl;
        if (typed != original)
        {
            _trustedThumbprint = null;
            _pendingThumbprint = null;
        }
        CertPanel.Visibility = Visibility.Collapsed;
        UpdateTrustedIndicator();
    }

    private bool IsHttps => ServerUrlBox.Text.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // Reduces what was typed to https://host:port/ and shows the cleaned address in the box, so a pasted link such as
    // https://host:5443/api/system/clinic can never end up saved as the server address. Null (with a message) if unusable.
    private string? ReadAddress()
    {
        if (string.IsNullOrWhiteSpace(ServerUrlBox.Text))
        {
            SetStatus((string)FindResource("ServerSettings.EnterAddressFirst"), isError: true);
            return null;
        }

        var address = Helpers.ServerAddress.Normalize(ServerUrlBox.Text);
        if (address is null)
        {
            SetStatus((string)FindResource("ServerSettings.InvalidAddress"), isError: true);
            return null;
        }

        if (ServerUrlBox.Text.Trim() != address) ServerUrlBox.Text = address; // (re-evaluates any pending trust via TextChanged)
        return address;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var url = ReadAddress();
        if (url is null) return;

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
        var url = ReadAddress();
        if (url is null) return;

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
