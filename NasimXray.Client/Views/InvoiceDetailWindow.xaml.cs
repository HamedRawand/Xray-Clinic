using NasimXray.Shared;
using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Billing;

namespace NasimXray.Client.Views;

public partial class InvoiceDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _invoiceId;
    private InvoiceDto? _invoice;

    public InvoiceDetailWindow(ApiClient apiClient, int invoiceId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _invoiceId = invoiceId;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _invoice = await _apiClient.GetInvoiceAsync(_invoiceId);
            Render();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private void Render()
    {
        if (_invoice is null) return;

        HeaderText.Text = $"{(string)FindResource("InvoiceDetail.TitlePrefix")}{_invoice.Id}";
        Title = HeaderText.Text;
        PatientText.Text = $"{_invoice.PatientName} ({_invoice.PatientCode})";

        TotalText.Text = Currency.Format(_invoice.Total);
        PaidText.Text = Currency.Format(_invoice.AmountPaid);
        BalanceText.Text = Currency.Format(_invoice.Balance);

        LinesPanel.Items.Clear();
        foreach (var line in _invoice.Lines)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel();
            name.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(line.ExamNumber) ? line.ExamTypeName : $"{line.ExamNumber}  {line.ExamTypeName}",
                FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            });
            if (line.DiscountAmount > 0)
            {
                name.Children.Add(new TextBlock
                {
                    Text = string.Format((string)FindResource("InvoiceDetail.DiscountFormat"),
                        Currency.Format(line.ListPrice), Currency.Format(line.DiscountAmount), line.DiscountReason),
                    FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush"),
                });
            }
            var price = new TextBlock { Text = Currency.Format(line.Price), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush") };
            Grid.SetColumn(price, 1);
            row.Children.Add(name);
            row.Children.Add(price);
            LinesPanel.Items.Add(row);
        }

        var isFullyPaid = _invoice.Balance <= 0;
        FullyPaidText.Visibility = isFullyPaid ? Visibility.Visible : Visibility.Collapsed;
        RecordPaymentButton.Visibility = isFullyPaid || !SessionService.Can(NasimXray.Shared.Enums.Permission.ProcessPayment) ? Visibility.Collapsed : Visibility.Visible;
        NoPaymentsText.Visibility = _invoice.Payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        PaymentsPanel.Items.Clear();
        foreach (var payment in _invoice.Payments)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var methodKey = $"PaymentMethod.{payment.Method}";
            var info = new TextBlock
            {
                Text = $"{Helpers.CalendarFormat.Format(payment.Timestamp, "datetime")} — {(string)FindResource(methodKey)} — {payment.ReceivedByName}",
                FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            };
            var amount = new TextBlock
            {
                Text = Currency.Format(payment.Amount), FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush"),
            };
            Grid.SetColumn(amount, 2);
            row.Children.Add(info);
            row.Children.Add(amount);
            PaymentsPanel.Items.Add(row);
        }
    }

    private async void RecordPayment_Click(object sender, RoutedEventArgs e)
    {
        if (_invoice is null) return;
        var dialog = new RecordPaymentDialog(_apiClient, _invoice) { Owner = this };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private void PrintReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (_invoice is null) return;
        try
        {
            PdfService.GenerateAndOpenReceipt(_invoice);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
