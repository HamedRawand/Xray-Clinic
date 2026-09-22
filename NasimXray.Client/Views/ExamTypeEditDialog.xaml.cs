using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamTypes;

namespace NasimXray.Client.Views;

public partial class ExamTypeEditDialog : Window
{
    private readonly ApiClient _apiClient;
    private int? _examTypeId;

    public ExamTypeEditDialog(ApiClient apiClient, ExamTypeDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _examTypeId = existing?.Id;

        if (existing is not null)
        {
            TitleText.Text = (string)FindResource("ExamTypes.Edit");
            Title = TitleText.Text;
            NameBox.Text = existing.Name;
            ModalityBox.Text = existing.Modality;
            BodyPartBox.Text = existing.BodyPart;
            PriceBox.Text = existing.Price.ToString(CultureInfo.InvariantCulture);
            ActiveBox.IsChecked = existing.IsActive;
            FieldsGrid.ItemsSource = existing.MeasurementFields;
            ShowFieldsSection();
        }
        else
        {
            TitleText.Text = (string)FindResource("ExamTypes.New");
            Title = TitleText.Text;
        }
    }

    private void ShowFieldsSection()
    {
        FieldsSection.Visibility = Visibility.Visible;
        NoFieldsText.Visibility = FieldsGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveBasic_Click(object sender, RoutedEventArgs e)
    {
        TopErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowTopError((string)FindResource("ExamTypes.NameRequired"));
            return;
        }
        if (!decimal.TryParse(PriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            price = 0;

        var request = new ExamTypeUpsertRequest
        {
            Name = NameBox.Text.Trim(),
            Modality = string.IsNullOrWhiteSpace(ModalityBox.Text) ? null : ModalityBox.Text.Trim(),
            BodyPart = string.IsNullOrWhiteSpace(BodyPartBox.Text) ? null : BodyPartBox.Text.Trim(),
            Price = price,
            IsActive = ActiveBox.IsChecked == true,
        };

        try
        {
            if (_examTypeId.HasValue)
            {
                await _apiClient.UpdateExamTypeAsync(_examTypeId.Value, request);
            }
            else
            {
                var created = await _apiClient.CreateExamTypeAsync(request);
                _examTypeId = created.Id;
                TitleText.Text = (string)FindResource("ExamTypes.Edit");
                Title = TitleText.Text;
                FieldsGrid.ItemsSource = created.MeasurementFields;
                ShowFieldsSection();
            }
        }
        catch (ApiException ex)
        {
            ShowTopError((string)FindResource($"Api.{ex.Reason}"));
        }
    }

    private async void AddField_Click(object sender, RoutedEventArgs e)
    {
        if (!_examTypeId.HasValue) return;
        var dialog = new MeasurementFieldEditDialog(_apiClient, _examTypeId.Value) { Owner = this };
        if (dialog.ShowDialog() == true)
            await ReloadFieldsAsync();
    }

    private async void FieldsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_examTypeId.HasValue || FieldsGrid.SelectedItem is not MeasurementFieldDto field) return;
        var dialog = new MeasurementFieldEditDialog(_apiClient, _examTypeId.Value, field) { Owner = this };
        if (dialog.ShowDialog() == true)
            await ReloadFieldsAsync();
    }

    private async Task ReloadFieldsAsync()
    {
        try
        {
            var types = await _apiClient.GetExamTypesAsync(includeInactive: true);
            var current = types.FirstOrDefault(t => t.Id == _examTypeId);
            FieldsGrid.ItemsSource = current?.MeasurementFields ?? [];
            NoFieldsText.Visibility = FieldsGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void ShowTopError(string message)
    {
        TopErrorText.Text = message;
        TopErrorText.Visibility = Visibility.Visible;
    }
}
