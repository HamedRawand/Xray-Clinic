using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NasimXray.Client.Services;
using NasimXray.Shared;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

public partial class InvoicesView : UserControl
{
    private const int PageSize = 50;

    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _searchDebounce;
    private int _page = 1;
    private int _requestSeq;
    private bool _ready;

    public InvoicesView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); _page = 1; await LoadAsync(); };

        Pager.PageRequested += async page => { _page = page; await LoadAsync(); };

        _ready = true;
        _ = LoadAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void StatusBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return; // fires once while the XAML loads
        _page = 1;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var seq = ++_requestSeq;
        try
        {
            var result = await _apiClient.GetInvoicesAsync(search: SearchBox.Text, status: CurrentStatus(), page: _page, pageSize: PageSize);
            if (seq != _requestSeq) return; // a newer request is already on its way

            _page = result.Page;
            Grid.ItemsSource = result.Items;
            EmptyText.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Pager.Update(result);

            TotalsCaption.Text = string.Format((string)FindResource("Invoices.TotalsFormat"), result.TotalCount);
            SumTotalText.Text = Currency.Format(result.SumTotal);
            SumPaidText.Text = Currency.Format(result.SumPaid);
            SumBalanceText.Text = Currency.Format(result.SumBalance);
            SumBalanceText.SetResourceReference(TextBlock.ForegroundProperty, result.SumBalance > 0 ? "DangerBrush" : "SuccessBrush");
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private InvoiceStatus? CurrentStatus() =>
        StatusBox.SelectedItem is ComboBoxItem { Tag: string tag } ? Enum.Parse<InvoiceStatus>(tag) : null;

    // Exports every invoice matching the current search/status (not just the visible page).
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var path = ExcelExportService.PromptPath(owner, $"Invoices_{DateTime.Today:yyyy-MM-dd}");
        if (path is null) return;

        try
        {
            var invoices = await _apiClient.ExportInvoicesAsync(SearchBox.Text, CurrentStatus());
            if (invoices.Count == 0)
            {
                MessageBox.Show(owner, (string)FindResource("Invoices.ExportEmpty"), (string)FindResource("App.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ExcelExportService.SaveInvoices(path, invoices);
            ExcelExportService.Open(path);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(owner, ex);
        }
        catch (IOException)
        {
            MessageBox.Show(owner, (string)FindResource("Stats.FileInUse"), (string)FindResource("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
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
