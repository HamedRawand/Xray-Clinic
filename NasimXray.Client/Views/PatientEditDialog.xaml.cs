using System.Windows;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Patients;

namespace NasimXray.Client.Views;

public partial class PatientEditDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int? _patientId;
    private uint _rowVersion;

    private record SexOption(string Value, string DisplayKey);

    public PatientEditDialog(ApiClient apiClient, PatientDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _patientId = existing?.Id;

        var sexOptions = new[]
        {
            new SexOption("Male", "Sex.Male"),
            new SexOption("Female", "Sex.Female"),
        };
        SexBox.ItemsSource = sexOptions
            .Select(o => new Helpers.ComboOption((string)FindResource(o.DisplayKey), o.Value))
            .ToList();
        SexBox.DisplayMemberPath = "Label";
        SexBox.SelectedValuePath = "Value";

        if (existing is not null)
        {
            TitleText.Text = (string)FindResource("PatientEdit.EditTitle");
            Title = TitleText.Text;
            FullNameBox.Text = existing.FullName;
            DobPicker.SelectedDate = existing.DateOfBirth;
            PhoneBox.Text = existing.PhoneNumber;
            AddressBox.Text = existing.Address;
            SexBox.SelectedValue = existing.Sex;
            _rowVersion = existing.RowVersion;
        }
        else
        {
            TitleText.Text = (string)FindResource("PatientEdit.NewTitle");
            Title = TitleText.Text;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(FullNameBox.Text))
        {
            ShowError((string)FindResource("PatientEdit.NameRequired"));
            return;
        }

        var request = new PatientUpsertRequest
        {
            FullName = FullNameBox.Text.Trim(),
            DateOfBirth = DobPicker.SelectedDate,
            Sex = SexBox.SelectedValue as string,
            PhoneNumber = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
            Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
            RowVersion = _rowVersion,
        };

        try
        {
            if (_patientId.HasValue)
                await _apiClient.UpdatePatientAsync(_patientId.Value, request);
            else
                await _apiClient.CreatePatientAsync(request);

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
