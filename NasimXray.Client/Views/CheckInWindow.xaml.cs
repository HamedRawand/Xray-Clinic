using NasimXray.Shared;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.CheckIn;
using NasimXray.Shared.Dtos.ExamTypes;
using NasimXray.Shared.Dtos.Patients;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

// Reception check-in on one screen: find or register the patient, tick the exams, take payment —
// saved in a single server call so a visit is never left half-created.
public partial class CheckInWindow : Window
{
    private enum PatientMode { Search, Selected, New }

    // One selectable exam. ChargeBox/FreeButton/ReasonPanel exist only for users who may give discounts.
    private sealed record ExamRow(CheckBox Box, string Name, decimal Unit,
        TextBox? ChargeBox, Button? FreeButton, FrameworkElement? ReasonPanel, TextBox? ReasonBox);

    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _searchDebounce;
    private readonly DispatcherTimer _duplicateDebounce;
    private readonly Dictionary<int, ExamRow> _examRows = [];

    private PatientDto? _patient;
    private PatientMode _mode = PatientMode.Search;
    private bool _amountEdited;
    private bool _settingAmount;
    private int _searchSeq;
    private int _duplicateSeq;
    private CheckInResponse? _result;

    // True once at least one visit was saved, so the caller can refresh its screen.
    public bool CheckedIn { get; private set; }

    public CheckInWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); await SearchPatientsAsync(); };
        _duplicateDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _duplicateDebounce.Tick += async (_, _) => { _duplicateDebounce.Stop(); await CheckDuplicatesAsync(); };

        // Only offer what this user is allowed to do.
        NewPatientButton.Visibility = SessionService.Can(Permission.ManagePatients) ? Visibility.Visible : Visibility.Collapsed;
        var canBill = SessionService.Can(Permission.ManageBilling);
        BillingCard.Visibility = canBill ? Visibility.Visible : Visibility.Collapsed;
        InvoiceCheck.IsChecked = canBill;
        PaymentPanel.Visibility = SessionService.Can(Permission.ProcessPayment) ? Visibility.Visible : Visibility.Collapsed;

        var sex = new[] { "Male", "Female" };
        SexBox.ItemsSource = sex.Select(s => new ComboOption((string)FindResource($"Sex.{s}"), s)).ToList();
        SexBox.DisplayMemberPath = "Label";
        SexBox.SelectedValuePath = "Value";

        MethodBox.ItemsSource = Enum.GetValues<PaymentMethod>()
            .Select(m => new ComboOption((string)FindResource($"PaymentMethod.{m}"), m)).ToList();
        MethodBox.DisplayMemberPath = "Label";
        MethodBox.SelectedValuePath = "Value";
        MethodBox.SelectedIndex = 0;

        ApplyMode(PatientMode.Search);
        UpdateTotal();
        Loaded += async (_, _) => { PatientSearchBox.Focus(); await LoadLookupsAsync(); };
    }

    private string Res(string key) => (string)FindResource(key);

    // ── Lookups ───────────────────────────────────────────────────────────

    private async Task LoadLookupsAsync()
    {
        try
        {
            var types = await _apiClient.GetExamTypesAsync();
            ExamList.Children.Clear();
            _examRows.Clear();

            foreach (var type in types.Where(t => t.IsActive).OrderBy(t => t.Modality).ThenBy(t => t.Name))
                ExamList.Children.Add(BuildExamRow(type));

            var physicians = new List<FilterItem> { new(Res("NewExamOrder.None"), null) };
            physicians.AddRange((await _apiClient.GetReferringPhysiciansAsync()).Select(p => new FilterItem(p.Name, p.Id)));
            PhysicianBox.ItemsSource = physicians;
            PhysicianBox.SelectedIndex = 0;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private sealed record FilterItem(string Label, int? Id)
    {
        public override string ToString() => Label;
    }

    private bool CanDiscount => SessionService.Can(Permission.ApplyDiscount);

    private FrameworkElement BuildExamRow(ExamTypeDto type)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 8), Tag = $"{type.Name} {type.Modality}".ToLowerInvariant() };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new CheckBox
        {
            Content = string.IsNullOrWhiteSpace(type.Modality) ? type.Name : $"{type.Name}  ·  {type.Modality}",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };

        var price = new TextBlock
        {
            Text = Currency.Format(type.Price),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextMutedBrush"),
        };
        Grid.SetColumn(price, 1);
        row.Children.Add(box);
        row.Children.Add(price);
        outer.Children.Add(row);

        TextBox? chargeBox = null, reasonBox = null;
        Button? freeButton = null;
        FrameworkElement? reasonPanel = null;

        if (CanDiscount)
        {
            chargeBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                Width = 90, Height = 30, FontSize = 12, Margin = new Thickness(12, 0, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                Text = type.Price.ToString("0.##", CultureInfo.InvariantCulture),
                Visibility = Visibility.Collapsed,
                ToolTip = Res("CheckIn.ChargeTip"),
            };
            Grid.SetColumn(chargeBox, 2);

            freeButton = new Button
            {
                Content = Res("CheckIn.Free"),
                Style = (Style)FindResource("SecondaryButton"),
                Height = 30, Padding = new Thickness(10, 0, 10, 0), FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            Grid.SetColumn(freeButton, 3);
            row.Children.Add(chargeBox);
            row.Children.Add(freeButton);

            reasonBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"), Height = 30, FontSize = 12, MaxLength = 250, MinWidth = 320,
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 6, 0, 0), Visibility = Visibility.Collapsed };
            panel.Children.Add(new TextBlock
            {
                Text = Res("CheckIn.DiscountReason"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecondaryBrush"), Margin = new Thickness(0, 0, 8, 0),
            });
            panel.Children.Add(reasonBox);
            reasonPanel = panel;
            outer.Children.Add(panel);

            chargeBox.TextChanged += (_, _) => { RefreshRow(type.Id); UpdateTotal(); };
            freeButton.Click += (_, _) => chargeBox.Text = "0";
        }

        box.Checked += (_, _) => { RefreshRow(type.Id); UpdateTotal(); };
        box.Unchecked += (_, _) => { RefreshRow(type.Id); UpdateTotal(); };

        _examRows[type.Id] = new ExamRow(box, type.Name, type.Price, chargeBox, freeButton, reasonPanel, reasonBox);
        return outer;
    }

    // Charge box / Free button show for ticked exams; the reason box only once the charge is below list price.
    private void RefreshRow(int typeId)
    {
        if (!_examRows.TryGetValue(typeId, out var row) || row.ChargeBox is null) return;
        var ticked = row.Box.IsChecked == true;
        row.ChargeBox.Visibility = ticked ? Visibility.Visible : Visibility.Collapsed;
        row.FreeButton!.Visibility = ticked ? Visibility.Visible : Visibility.Collapsed;
        row.ReasonPanel!.Visibility = ticked && TryGetCharge(row, out var charge) && charge < row.Unit
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // The amount this exam will be charged; false if the box holds something that isn't a valid amount.
    private static bool TryGetCharge(ExamRow row, out decimal charge)
    {
        charge = row.Unit;
        if (row.ChargeBox is null) return true;
        var text = row.ChargeBox.Text.Trim();
        if (text.Length == 0) return true;
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out charge);
    }

    private void ExamFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var term = ExamFilterBox.Text.Trim().ToLowerInvariant();
        foreach (FrameworkElement row in ExamList.Children)
            row.Visibility = term.Length == 0 || ((string)row.Tag).Contains(term) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Patient: find / pick / register ───────────────────────────────────

    private void ApplyMode(PatientMode mode)
    {
        _mode = mode;
        SearchPanel.Visibility = mode == PatientMode.Search ? Visibility.Visible : Visibility.Collapsed;
        SelectedPanel.Visibility = mode == PatientMode.Selected ? Visibility.Visible : Visibility.Collapsed;
        NewPanel.Visibility = mode == PatientMode.New ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PatientSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async Task SearchPatientsAsync()
    {
        var text = PatientSearchBox.Text.Trim();
        var seq = ++_searchSeq;
        if (text.Length < 2)
        {
            PatientResults.ItemsSource = null;
            PatientResults.Visibility = Visibility.Collapsed;
            NoResultsText.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var patients = (await _apiClient.GetPatientsAsync(text, page: 1, pageSize: 20)).Items;
            if (seq != _searchSeq) return;
            PatientResults.ItemsSource = patients;
            PatientResults.Visibility = patients.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoResultsText.Visibility = patients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private void PatientResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatientResults.SelectedItem is PatientDto patient)
            SelectPatient(patient);
    }

    private void SelectPatient(PatientDto patient)
    {
        _patient = patient;
        SelectedCodeText.Text = patient.PatientCode;
        SelectedNameText.Text = patient.FullName;
        SelectedPhoneText.Text = patient.PhoneNumber;
        PatientResults.SelectedItem = null;
        DuplicatePanel.Visibility = Visibility.Collapsed;
        ApplyMode(PatientMode.Selected);
        HideError();
    }

    private void ChangePatient_Click(object sender, RoutedEventArgs e)
    {
        _patient = null;
        PatientSearchBox.Clear();
        PatientResults.ItemsSource = null;
        PatientResults.Visibility = Visibility.Collapsed;
        ApplyMode(PatientMode.Search);
        PatientSearchBox.Focus();
    }

    private void NewPatient_Click(object sender, RoutedEventArgs e)
    {
        // Carry over what was typed in the search box: digits look like a phone number, otherwise a name.
        var typed = PatientSearchBox.Text.Trim();
        if (typed.Length > 0 && string.IsNullOrWhiteSpace(FullNameBox.Text) && string.IsNullOrWhiteSpace(PhoneBox.Text))
        {
            if (typed.Count(char.IsDigit) >= typed.Length / 2 + 1) PhoneBox.Text = typed;
            else if (!typed.StartsWith("P-", StringComparison.OrdinalIgnoreCase)) FullNameBox.Text = typed;
        }

        _patient = null;
        ApplyMode(PatientMode.New);
        FullNameBox.Focus();
    }

    private void BackToSearch_Click(object sender, RoutedEventArgs e) => ApplyMode(PatientMode.Search);

    private void NewPatientField_Changed(object sender, TextChangedEventArgs e)
    {
        _duplicateDebounce.Stop();
        _duplicateDebounce.Start();
    }

    // Soft warning only: the user decides whether a similar record is the same person.
    private async Task CheckDuplicatesAsync()
    {
        if (_mode != PatientMode.New) return;
        var seq = ++_duplicateSeq;
        try
        {
            var matches = await _apiClient.GetPossibleDuplicatePatientsAsync(FullNameBox.Text, PhoneBox.Text);
            if (seq != _duplicateSeq) return;
            DuplicateList.ItemsSource = matches;
            DuplicatePanel.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException)
        {
            DuplicatePanel.Visibility = Visibility.Collapsed; // the check is advisory; never block registration on it
        }
    }

    private void UseDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PatientDto patient)
            SelectPatient(patient);
    }

    // ── Totals / payment ──────────────────────────────────────────────────

    private decimal Total() => _examRows.Values.Where(r => r.Box.IsChecked == true)
        .Sum(r => TryGetCharge(r, out var charge) && charge >= 0 && charge <= r.Unit ? charge : r.Unit);

    private bool TryGetAmount(out decimal amount)
    {
        var text = AmountBox.Text.Trim();
        if (text.Length == 0) { amount = 0; return true; }
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private bool BillingActive => BillingCard.Visibility == Visibility.Visible && InvoiceCheck.IsChecked == true;

    private bool PaymentActive => BillingActive && PaymentPanel.Visibility == Visibility.Visible;

    private void UpdateTotal()
    {
        if (TotalText is null) return;
        var total = Total();
        TotalText.Text = $"{Res("CheckIn.Total")}{Currency.Format(total)}";

        // Until the receptionist types an amount, assume the patient pays in full.
        if (!_amountEdited)
        {
            _settingAmount = true;
            AmountBox.Text = total.ToString("0.##", CultureInfo.InvariantCulture);
            _settingAmount = false;
        }
        UpdateBalance();
    }

    private void UpdateBalance()
    {
        if (BalanceText is null) return;
        if (!PaymentActive) { BalanceText.Text = string.Empty; return; }

        if (!TryGetAmount(out var amount)) { BalanceText.Text = string.Empty; return; }
        var balance = Total() - amount;
        BalanceText.Text = $"{Res("CheckIn.BalanceAfter")}{Currency.Format(balance)}";
        BalanceText.Foreground = (Brush)FindResource(balance > 0 ? "DangerBrush" : "SuccessBrush");
    }

    private void AmountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingAmount) return;
        _amountEdited = true;
        UpdateBalance();
    }

    private void FullAmount_Click(object sender, RoutedEventArgs e)
    {
        _amountEdited = false;
        UpdateTotal();
    }

    private void Invoice_Toggled(object sender, RoutedEventArgs e)
    {
        if (PaymentPanel is null) return; // fires once during InitializeComponent
        PaymentPanel.IsEnabled = InvoiceCheck.IsChecked == true;
        PaymentPanel.Opacity = PaymentPanel.IsEnabled ? 1 : 0.5;
        UpdateBalance();
    }

    // ── Submit ────────────────────────────────────────────────────────────

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        var request = new CheckInRequest();

        if (_mode == PatientMode.Selected && _patient is not null)
        {
            request.PatientId = _patient.Id;
        }
        else if (_mode == PatientMode.New)
        {
            if (string.IsNullOrWhiteSpace(FullNameBox.Text))
            {
                ShowError(Res("PatientEdit.NameRequired"));
                return;
            }
            request.NewPatient = new PatientUpsertRequest
            {
                FullName = FullNameBox.Text.Trim(),
                DateOfBirth = DobPicker.SelectedDate,
                Sex = SexBox.SelectedValue as string,
                PhoneNumber = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
                Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
            };
        }
        else
        {
            ShowError(Res("CheckIn.ChoosePatient"));
            return;
        }

        foreach (var (typeId, row) in _examRows.Where(kv => kv.Value.Box.IsChecked == true))
        {
            var item = new CheckInExamItem { ExamTypeId = typeId };
            if (!TryGetCharge(row, out var charge) || charge < 0 || charge > row.Unit)
            {
                ShowError(string.Format(Res("CheckIn.InvalidCharge"), row.Name, Currency.Format(row.Unit)));
                return;
            }
            if (charge < row.Unit)
            {
                var reason = row.ReasonBox?.Text.Trim() ?? string.Empty;
                if (reason.Length == 0)
                {
                    ShowError(string.Format(Res("CheckIn.DiscountReasonRequired"), row.Name));
                    return;
                }
                item.Charge = charge;
                item.DiscountReason = reason;
            }
            request.Exams.Add(item);
        }
        if (request.Exams.Count == 0)
        {
            ShowError(Res("CheckIn.ChooseExam"));
            return;
        }
        request.ReferringPhysicianId = (PhysicianBox.SelectedItem as FilterItem)?.Id;

        request.CreateInvoice = BillingActive;
        if (PaymentActive)
        {
            if (!TryGetAmount(out var amount) || amount < 0 || amount > Total())
            {
                ShowError(Res("CheckIn.InvalidAmount"));
                return;
            }
            request.PaymentAmount = amount;
            request.PaymentMethod = MethodBox.SelectedValue is PaymentMethod m ? m : PaymentMethod.Cash;
        }

        SubmitButton.IsEnabled = false;
        try
        {
            _result = await _apiClient.CheckInAsync(request);
            CheckedIn = true;
            ShowResult(_result);
        }
        catch (ApiException ex)
        {
            var message = (string)(TryFindResource($"Api.{ex.Reason}") ?? ex.Message);
            if (ex.Reason == ApiErrorReason.BadRequest && !string.IsNullOrWhiteSpace(ex.Detail))
                message += " " + ex.Detail;
            ShowError(message);
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private void ShowResult(CheckInResponse result)
    {
        ResultPatientText.Text = $"{result.Patient.PatientCode} · {result.Patient.FullName}" +
                                 (result.PatientCreated ? $"  ({Res("CheckIn.NewTag")})" : string.Empty);

        ResultExams.Items.Clear();
        foreach (var exam in result.Exams)
        {
            ResultExams.Items.Add(new TextBlock
            {
                Text = $"{exam.ExamNumber}  —  {exam.ExamTypeName}",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 3),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            });
        }

        if (result.Invoice is { } invoice)
        {
            ResultInvoiceText.Text = $"{Res("CheckIn.InvoiceNo")}{invoice.Id}   ·   {Res("CheckIn.InvTotal")}{Currency.Format(invoice.Total)}   ·   " +
                                     $"{Res("CheckIn.InvPaid")}{Currency.Format(invoice.AmountPaid)}   ·   {Res("CheckIn.InvBalance")}{Currency.Format(invoice.Balance)}";
            PrintButton.Visibility = Visibility.Visible;
        }
        else
        {
            ResultInvoiceText.Text = Res("CheckIn.NoInvoice");
            PrintButton.Visibility = Visibility.Collapsed;
        }

        OpenPatientButton.Visibility = SessionService.Can(Permission.ViewPatients) ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_result?.Invoice is null) return;
        try
        {
            PdfService.GenerateAndOpenReceipt(_result.Invoice);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Res("App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenPatient_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        new PatientDetailWindow(_apiClient, _result.Patient.Id) { Owner = this }.ShowDialog();
    }

    private void NewCheckIn_Click(object sender, RoutedEventArgs e)
    {
        _result = null;
        _patient = null;
        _amountEdited = false;

        PatientSearchBox.Clear();
        PatientResults.ItemsSource = null;
        PatientResults.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        FullNameBox.Clear();
        PhoneBox.Clear();
        AddressBox.Clear();
        DobPicker.SelectedDate = null;
        SexBox.SelectedIndex = -1;
        DuplicatePanel.Visibility = Visibility.Collapsed;

        ExamFilterBox.Clear();
        foreach (var row in _examRows.Values)
        {
            row.Box.IsChecked = false;
            row.ChargeBox?.SetCurrentValue(TextBox.TextProperty, row.Unit.ToString("0.##", CultureInfo.InvariantCulture));
            row.ReasonBox?.Clear();
        }
        if (PhysicianBox.Items.Count > 0) PhysicianBox.SelectedIndex = 0;
        InvoiceCheck.IsChecked = BillingCard.Visibility == Visibility.Visible;

        ApplyMode(PatientMode.Search);
        UpdateTotal();
        HideError();

        ResultPanel.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Visible;
        PatientSearchBox.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
