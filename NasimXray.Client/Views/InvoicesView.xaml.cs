using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Billing;

namespace NasimXray.Client.Views;

public partial class InvoicesView : UserControl
{
    private readonly ApiClient _apiClient;

    public InvoicesView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var invoices = await _apiClient.GetInvoicesAsync();
            Grid.ItemsSource = invoices;
            EmptyText.Visibility = invoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is InvoiceDto invoice) await OpenAsync(invoice);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is InvoiceDto invoice) await OpenAsync(invoice);
    }

    private async Task OpenAsync(InvoiceDto invoice)
    {
        var window = new InvoiceDetailWindow(_apiClient, invoice.Id) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        await LoadAsync();
    }
}
