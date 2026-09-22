using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.ExamTypes;

namespace NasimXray.Client.Views;

public partial class NewInvoiceDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _patientId;
    private readonly Dictionary<int, CheckBox> _checkboxes = [];
    private readonly Dictionary<int, decimal> _prices = [];

    public NewInvoiceDialog(ApiClient apiClient, int patientId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _patientId = patientId;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var orders = await _apiClient.GetExamOrdersAsync(patientId: _patientId, uninvoiced: true);
            var examTypes = await _apiClient.GetExamTypesAsync(includeInactive: true);
            var priceByExamType = examTypes.ToDictionary(t => t.Id, t => t.Price);

            OrdersPanel.Items.Clear();
            _checkboxes.Clear();
            _prices.Clear();

            foreach (var order in orders)
            {
                var price = priceByExamType.GetValueOrDefault(order.ExamTypeId);
                _prices[order.Id] = price;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var checkBox = new CheckBox
                {
                    Content = order.ExamTypeName,
                    FontSize = 13,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                    IsChecked = true,
                };
                checkBox.Checked += (_, _) => UpdateTotal();
                checkBox.Unchecked += (_, _) => UpdateTotal();
                _checkboxes[order.Id] = checkBox;

                var priceText = new TextBlock
                {
                    Text = price.ToString("N2"),
                    FontSize = 12,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(priceText, 1);

                row.Children.Add(checkBox);
                row.Children.Add(priceText);
                OrdersPanel.Items.Add(row);
            }

            NoneText.Visibility = orders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateTotal();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private void UpdateTotal()
    {
        var total = _checkboxes.Where(kv => kv.Value.IsChecked == true).Sum(kv => _prices[kv.Key]);
        TotalText.Text = $"{(string)FindResource("NewInvoice.Total")}{total:N2}";
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var selected = _checkboxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        if (selected.Count == 0)
        {
            ShowError((string)FindResource("NewInvoice.SelectAtLeastOne"));
            return;
        }

        try
        {
            await _apiClient.CreateInvoiceAsync(new InvoiceCreateRequest { PatientId = _patientId, ExamOrderIds = selected });
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
