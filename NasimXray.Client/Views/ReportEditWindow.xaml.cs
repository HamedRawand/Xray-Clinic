using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.ExamTypes;
using NasimXray.Shared.Dtos.Patients;
using NasimXray.Shared.Dtos.Reports;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

public partial class ReportEditWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly ExamOrderDto _order;
    private PatientDto? _patient;
    private readonly Dictionary<int, TextBox> _measurementBoxes = [];
    private List<MeasurementFieldDto> _fieldDefinitions = [];
    private ReportDto? _report;
    private bool _amendMode;

    public ReportEditWindow(ApiClient apiClient, ExamOrderDto order)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _order = order;

        HeaderText.Text = order.ExamTypeName;
        SubHeaderText.Text = $"{order.PatientName}  •  {order.Modality}";

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var examTypes = await _apiClient.GetExamTypesAsync();
            _fieldDefinitions = examTypes.FirstOrDefault(t => t.Id == _order.ExamTypeId)?.MeasurementFields ?? [];

            _patient = await _apiClient.GetPatientAsync(_order.PatientId);
            _report = await _apiClient.GetReportAsync(_order.Id);

            BuildMeasurementInputs();
            PopulateFromReport();
            RefreshHistory();
            ApplyUiState();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private void BuildMeasurementInputs()
    {
        _measurementBoxes.Clear();
        var panel = new StackPanel();

        foreach (var field in _fieldDefinitions.OrderBy(f => f.DisplayOrder))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            var label = new TextBlock
            {
                Text = field.Unit is null ? field.Label : $"{field.Label} ({field.Unit})",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            };
            Grid.SetColumn(label, 0);

            var box = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                FlowDirection = FlowDirection.LeftToRight,
            };
            Grid.SetColumn(box, 1);
            _measurementBoxes[field.Id] = box;

            row.Children.Add(label);
            row.Children.Add(box);
            panel.Children.Add(row);
        }

        MeasurementsPanel.Items.Clear();
        MeasurementsPanel.Items.Add(panel);
        MeasurementsSection.Visibility = _fieldDefinitions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateFromReport()
    {
        FindingsBox.Text = _report?.Findings;
        ImpressionBox.Text = _report?.Impression;

        foreach (var (fieldId, box) in _measurementBoxes)
        {
            var value = _report?.Measurements.FirstOrDefault(m => m.FieldDefinitionId == fieldId)?.Value;
            box.Text = value;
        }
    }

    private void RefreshHistory()
    {
        HistoryPanel.Items.Clear();
        if (_report is null || _report.Amendments.Count == 0)
        {
            HistorySection.Visibility = Visibility.Collapsed;
            return;
        }

        HistorySection.Visibility = Visibility.Visible;
        foreach (var amendment in _report.Amendments)
        {
            var entry = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            entry.Children.Add(new TextBlock
            {
                Text = $"{amendment.AmendedAt:yyyy-MM-dd HH:mm} — {amendment.AmendedByName}",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            });
            entry.Children.Add(new TextBlock
            {
                Text = amendment.Reason, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            });
            HistoryPanel.Items.Add(entry);
        }
    }

    private void ApplyUiState()
    {
        var isFinalized = _report?.IsFinalized == true;
        var editable = !isFinalized || _amendMode;

        FindingsBox.IsReadOnly = !editable;
        ImpressionBox.IsReadOnly = !editable;
        foreach (var box in _measurementBoxes.Values) box.IsReadOnly = !editable;

        FinalizedBanner.Visibility = isFinalized && !_amendMode ? Visibility.Visible : Visibility.Collapsed;
        AmendReasonPanel.Visibility = _amendMode ? Visibility.Visible : Visibility.Collapsed;

        SaveDraftButton.Visibility = !isFinalized ? Visibility.Visible : Visibility.Collapsed;
        FinalizeButton.Visibility = !isFinalized ? Visibility.Visible : Visibility.Collapsed;
        AmendReasonPanelToggle.Visibility = isFinalized && !_amendMode ? Visibility.Visible : Visibility.Collapsed;
        SubmitAmendButton.Visibility = _amendMode ? Visibility.Visible : Visibility.Collapsed;
        PrintButton.Visibility = _report is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null || _patient is null)
        {
            MessageBox.Show(this, (string)FindResource("Report.NothingToPrint"), (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            PdfService.GenerateAndOpen(_report, _order, _patient);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<MeasurementValueRequest> CollectMeasurements() =>
        _measurementBoxes.Select(kv => new MeasurementValueRequest { FieldDefinitionId = kv.Key, Value = kv.Value.Text }).ToList();

    private async void SaveDraft_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _report = await _apiClient.SaveReportDraftAsync(_order.Id, new ReportSaveRequest
            {
                Findings = FindingsBox.Text,
                Impression = ImpressionBox.Text,
                Measurements = CollectMeasurements(),
                RowVersion = _report?.RowVersion,
            });
            MessageBox.Show(this, (string)FindResource("Report.Saved"), (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private async void Finalize_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, (string)FindResource("Report.ConfirmFinalize"), (string)FindResource("App.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            // Save current form state first so Finalize always locks in what's on screen.
            _report = await _apiClient.SaveReportDraftAsync(_order.Id, new ReportSaveRequest
            {
                Findings = FindingsBox.Text,
                Impression = ImpressionBox.Text,
                Measurements = CollectMeasurements(),
                RowVersion = _report?.RowVersion,
            });

            _report = await _apiClient.FinalizeReportAsync(_order.Id, new ReportFinalizeRequest { RowVersion = _report.RowVersion });

            PopulateFromReport();
            RefreshHistory();
            ApplyUiState();
            MessageBox.Show(this, (string)FindResource("Report.FinalizedMessage"), (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private void AmendToggle_Click(object sender, RoutedEventArgs e)
    {
        _amendMode = true;
        ApplyUiState();
    }

    private async void SubmitAmend_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AmendReasonBox.Text))
        {
            MessageBox.Show(this, (string)FindResource("Report.AmendReasonRequired"), (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _report = await _apiClient.AmendReportAsync(_order.Id, new ReportAmendRequest
            {
                Findings = FindingsBox.Text,
                Impression = ImpressionBox.Text,
                Measurements = CollectMeasurements(),
                Reason = AmendReasonBox.Text.Trim(),
                RowVersion = _report!.RowVersion,
            });

            _amendMode = false;
            AmendReasonBox.Clear();
            PopulateFromReport();
            RefreshHistory();
            ApplyUiState();
            MessageBox.Show(this, (string)FindResource("Report.AmendedMessage"), (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
