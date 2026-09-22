using System.Windows;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ReferringPhysicians;

namespace NasimXray.Client.Views;

public partial class ReferringPhysicianEditDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int? _id;

    public ReferringPhysicianEditDialog(ApiClient apiClient, ReferringPhysicianDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _id = existing?.Id;

        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            ClinicBox.Text = existing.ClinicName;
            PhoneBox.Text = existing.PhoneNumber;
            ActiveBox.IsChecked = existing.IsActive;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError((string)FindResource("RefPhysicians.NameRequired"));
            return;
        }

        var request = new ReferringPhysicianUpsertRequest
        {
            Name = NameBox.Text.Trim(),
            ClinicName = string.IsNullOrWhiteSpace(ClinicBox.Text) ? null : ClinicBox.Text.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
            IsActive = ActiveBox.IsChecked == true,
        };

        try
        {
            if (_id.HasValue)
                await _apiClient.UpdateReferringPhysicianAsync(_id.Value, request);
            else
                await _apiClient.CreateReferringPhysicianAsync(request);

            DialogResult = true;
            Close();
        }
        catch (ApiException ex)
        {
            ShowError((string)FindResource($"Api.{ex.Reason}"));
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
