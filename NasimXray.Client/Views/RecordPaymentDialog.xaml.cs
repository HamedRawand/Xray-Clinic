using NasimXray.Shared;
using System.Globalization;
using System.Windows;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

public partial class RecordPaymentDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly InvoiceDto _invoice;

    public RecordPaymentDialog(ApiClient apiClient, InvoiceDto invoice)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _invoice = invoice;

        BalanceText.Text = $"{(string)FindResource("RecordPayment.Balance")}{Currency.Format(invoice.Balance)}";
        AmountBox.Text = invoice.Balance.ToString("N2", CultureInfo.InvariantCulture);

        MethodBox.ItemsSource = Enum.GetValues<PaymentMethod>()
            .Select(m => new Helpers.ComboOption((string)FindResource($"PaymentMethod.{m}"), m))
            .ToList();
        MethodBox.DisplayMemberPath = "Label";
        MethodBox.SelectedValuePath = "Value";
        MethodBox.SelectedIndex = 0;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ShowError((string)FindResource("RecordPayment.InvalidAmount"));
            return;
        }

        try
        {
            await _apiClient.RecordPaymentAsync(_invoice.Id, new PaymentCreateRequest
            {
                Amount = amount,
                Method = (PaymentMethod)MethodBox.SelectedValue,
            });
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
