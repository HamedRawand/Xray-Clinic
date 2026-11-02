using NasimXray.Shared;
using System.Globalization;
using System.Windows;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamOrders;

namespace NasimXray.Client.Views;

// Set, change or remove the discount on one exam that has not been invoiced yet. The patient is charged
// exactly what is typed here (0 = free); anything below the list price needs a reason.
public partial class ExamDiscountDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly ExamOrderDto _order;

    public ExamDiscountDialog(ApiClient apiClient, ExamOrderDto order)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _order = order;

        ExamText.Text = $"{order.ExamNumber} · {order.PatientName} · {order.ExamTypeName}";
        ListPriceText.Text = $"{Res("ExamDiscount.ListPrice")}{Currency.Format(order.Price)}";
        ChargeBox.Text = order.NetPrice.ToString("0.##", CultureInfo.InvariantCulture);
        ReasonBox.Text = order.DiscountReason;

        if (order.DiscountAmount > 0)
        {
            CurrentText.Text = string.Format(Res("ExamDiscount.CurrentFormat"),
                Currency.Format(order.DiscountAmount), order.DiscountedByName ?? "—",
                order.DiscountedAt is { } at ? CalendarFormat.Format(at, "datetime") : string.Empty);
            CurrentText.Visibility = Visibility.Visible;
            RemoveButton.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => { ChargeBox.Focus(); ChargeBox.SelectAll(); };
    }

    private string Res(string key) => (string)FindResource(key);

    private void Free_Click(object sender, RoutedEventArgs e) => ChargeBox.Text = "0";

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (!decimal.TryParse(ChargeBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var charge) ||
            charge < 0 || charge > _order.Price)
        {
            ShowError(string.Format(Res("ExamDiscount.InvalidCharge"), Currency.Format(_order.Price)));
            return;
        }

        var reason = ReasonBox.Text.Trim();
        if (charge < _order.Price && reason.Length == 0)
        {
            ShowError(Res("ExamDiscount.ReasonRequired"));
            return;
        }

        await SubmitAsync(charge, charge < _order.Price ? reason : null);
    }

    private async void Remove_Click(object sender, RoutedEventArgs e) => await SubmitAsync(_order.Price, null);

    private async Task SubmitAsync(decimal charge, string? reason)
    {
        try
        {
            await _apiClient.UpdateExamDiscountAsync(_order.Id, new ExamDiscountRequest
            {
                Charge = charge,
                Reason = reason,
                RowVersion = _order.RowVersion,
            });
            DialogResult = true;
            Close();
        }
        catch (ApiException ex)
        {
            var message = (string)(TryFindResource($"Api.{ex.Reason}") ?? ex.Message);
            if (ex.Reason == ApiErrorReason.BadRequest && !string.IsNullOrWhiteSpace(ex.Detail))
                message += " " + ex.Detail;
            ShowError(message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
