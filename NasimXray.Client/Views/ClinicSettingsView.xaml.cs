using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Clinic;

namespace NasimXray.Client.Views;

// Admin screen where the clinic enters its own name, contact details and logo. Saving updates this PC at once;
// the other PCs pick it up within a couple of minutes (they refresh periodically).
public partial class ClinicSettingsView : UserControl
{
    private const int MaxLogoBytes = 300 * 1024;

    private readonly ApiClient _apiClient;
    private uint _rowVersion;

    public ClinicSettingsView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadAsync();
    }

    private string Res(string key) => (string)FindResource(key);

    private async Task LoadAsync()
    {
        try
        {
            var profile = await _apiClient.GetClinicProfileAsync();
            Fill(profile);
            ShowLogo(profile.HasLogo ? await _apiClient.GetClinicLogoAsync() : null);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private void Fill(ClinicProfileDto profile)
    {
        _rowVersion = profile.RowVersion;
        // A fresh install shows the neutral placeholder; start with an empty box so the admin types the real name.
        NameEnglishBox.Text = profile.IsConfigured ? profile.NameEnglish : string.Empty;
        NameDariBox.Text = profile.NameDari;
        TaglineBox.Text = profile.Tagline;
        AddressBox.Text = profile.Address;
        PhoneBox.Text = profile.Phone;
        EmailBox.Text = profile.Email;
    }

    private void ShowLogo(byte[]? bytes)
    {
        var image = ClinicInfoService.ToImage(bytes);
        LogoPreview.Source = image;
        NoLogoText.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        RemoveLogoButton.IsEnabled = image is not null;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        if (string.IsNullOrWhiteSpace(NameEnglishBox.Text))
        {
            ShowStatus(Res("ClinicSettings.NameRequired"), error: true);
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            var saved = await _apiClient.UpdateClinicProfileAsync(new ClinicProfileUpdateRequest
            {
                NameEnglish = NameEnglishBox.Text.Trim(),
                NameDari = NameDariBox.Text,
                Tagline = TaglineBox.Text,
                Address = AddressBox.Text,
                Phone = PhoneBox.Text,
                Email = EmailBox.Text,
                RowVersion = _rowVersion,
            });
            _rowVersion = saved.RowVersion;

            await ClinicInfoService.RefreshAsync(_apiClient);
            ShowStatus(Res("ClinicSettings.Saved"), error: false);
        }
        catch (ApiException ex)
        {
            ShowStatus(ErrorText(ex), error: true);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void ChooseLogo_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        var dialog = new OpenFileDialog
        {
            Title = Res("ClinicSettings.ChooseLogo"),
            Filter = "PNG / JPEG (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(dialog.FileName);
        }
        catch (IOException)
        {
            ShowStatus(Res("ClinicSettings.LogoBadType"), error: true);
            return;
        }

        if (bytes.Length > MaxLogoBytes)
        {
            ShowStatus(Res("ClinicSettings.LogoTooLarge"), error: true);
            return;
        }
        if (ClinicInfoService.ToImage(bytes) is null)
        {
            ShowStatus(Res("ClinicSettings.LogoBadType"), error: true);
            return;
        }

        try
        {
            var saved = await _apiClient.UploadClinicLogoAsync(bytes);
            _rowVersion = saved.RowVersion; // the logo save changed the row; keep Save from hitting a stale version
            ShowLogo(bytes);
            await ClinicInfoService.RefreshAsync(_apiClient);
        }
        catch (ApiException ex)
        {
            ShowStatus(ErrorText(ex), error: true);
        }
    }

    private async void RemoveLogo_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        try
        {
            var saved = await _apiClient.RemoveClinicLogoAsync();
            _rowVersion = saved.RowVersion;
            ShowLogo(null);
            await ClinicInfoService.RefreshAsync(_apiClient);
        }
        catch (ApiException ex)
        {
            ShowStatus(ErrorText(ex), error: true);
        }
    }

    private string ErrorText(ApiException ex)
    {
        var message = (string)(TryFindResource($"Api.{ex.Reason}") ?? ex.Message);
        if (ex.Reason == ApiErrorReason.BadRequest && !string.IsNullOrWhiteSpace(ex.Detail))
            message += " " + ex.Detail;
        return message;
    }

    private void ShowStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "SuccessBrush");
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;
}
