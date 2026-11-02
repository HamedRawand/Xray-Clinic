using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamOrders;

namespace NasimXray.Client.Views;

public partial class PatientDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _patientId;

    public PatientDetailWindow(ApiClient apiClient, int patientId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _patientId = patientId;

        NewInvoiceButton.Visibility = SessionService.Can(NasimXray.Shared.Enums.Permission.ManageBilling) ? Visibility.Visible : Visibility.Collapsed;
        NewExamOrderButton.Visibility = SessionService.Can(NasimXray.Shared.Enums.Permission.ManageExamOrders) ? Visibility.Visible : Visibility.Collapsed;
        DiscountColumn.Visibility = SessionService.Can(NasimXray.Shared.Enums.Permission.ApplyDiscount) ? Visibility.Visible : Visibility.Collapsed;
        ReportColumn.Visibility = SessionService.Can(NasimXray.Shared.Enums.Permission.ViewReports) ? Visibility.Visible : Visibility.Collapsed;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var patient = await _apiClient.GetPatientAsync(_patientId);
            PatientNameText.Text = $"{(string)FindResource("PatientDetail.TitlePrefix")}{patient.FullName}";
            PatientCodeText.Text = patient.PatientCode;
            Title = PatientNameText.Text;

            var orders = await _apiClient.GetExamOrdersAsync(patientId: _patientId);
            Grid.ItemsSource = orders;
            EmptyText.Visibility = orders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private async void NewInvoice_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewInvoiceDialog(_apiClient, _patientId) { Owner = this };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private async void NewExamOrder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewExamOrderDialog(_apiClient, _patientId) { Owner = this };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private async void Discount_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExamOrderDto order) return;
        var dialog = new ExamDiscountDialog(_apiClient, order) { Owner = this };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private async void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExamOrderDto order) return;
        var window = new ReportEditWindow(_apiClient, order) { Owner = this };
        window.ShowDialog();
        await LoadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
