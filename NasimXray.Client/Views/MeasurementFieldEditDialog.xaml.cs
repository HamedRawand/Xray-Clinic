using System.Windows;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamTypes;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

public partial class MeasurementFieldEditDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _examTypeId;
    private readonly int? _fieldId;

    public MeasurementFieldEditDialog(ApiClient apiClient, int examTypeId, MeasurementFieldDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _examTypeId = examTypeId;
        _fieldId = existing?.Id;

        DataTypeBox.ItemsSource = Enum.GetValues<MeasurementDataType>();
        DataTypeBox.SelectedItem = existing?.DataType ?? MeasurementDataType.Number;

        if (existing is not null)
        {
            LabelBox.Text = existing.Label;
            KeyBox.Text = existing.FieldKey;
            UnitBox.Text = existing.Unit;
            OrderBox.Text = existing.DisplayOrder.ToString();
            ActiveBox.IsChecked = existing.IsActive;
        }
        else
        {
            OrderBox.Text = "0";
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(LabelBox.Text) || string.IsNullOrWhiteSpace(KeyBox.Text))
        {
            ShowError((string)FindResource("ExamTypes.NameRequired"));
            return;
        }

        var request = new MeasurementFieldUpsertRequest
        {
            Label = LabelBox.Text.Trim(),
            FieldKey = KeyBox.Text.Trim(),
            Unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? null : UnitBox.Text.Trim(),
            DataType = (MeasurementDataType)DataTypeBox.SelectedItem,
            DisplayOrder = int.TryParse(OrderBox.Text, out var order) ? order : 0,
            IsActive = ActiveBox.IsChecked == true,
        };

        try
        {
            if (_fieldId.HasValue)
                await _apiClient.UpdateMeasurementFieldAsync(_examTypeId, _fieldId.Value, request);
            else
                await _apiClient.AddMeasurementFieldAsync(_examTypeId, request);

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
