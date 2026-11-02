using NasimXray.Shared;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Stats;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

// Period report: totals, breakdowns by exam type / modality / referring physician / day, and (for
// billing users) money. Everything on screen can be exported to one Excel workbook.
public partial class StatisticsView : UserControl
{
    private sealed record StatRow(string Label, int Exams, int Patients, decimal? Revenue, decimal? Discount);
    private sealed record DayRow(string Date, int Exams, int Patients, decimal? Collected);

    private readonly ApiClient _apiClient;
    private readonly PersianCalendar _persian = new();
    private StatsReportDto? _report;
    private int _requestSeq;
    private bool _suppress = true;

    public StatisticsView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        // Money columns/sections only exist for users allowed to see billing.
        var money = SessionService.Can(Permission.ViewBilling);
        var moneyVisibility = money ? Visibility.Visible : Visibility.Collapsed;
        TypeRevenue.Visibility = ModalityRevenue.Visibility = DoctorRevenue.Visibility = DayCollected.Visibility = moneyVisibility;
        TypeDiscount.Visibility = ModalityDiscount.Visibility = DoctorDiscount.Visibility = moneyVisibility;
        NoteText.Visibility = moneyVisibility;

        Loaded += async (_, _) =>
        {
            AppSession.LanguageChanged += Render;
            AppSession.CalendarTypeChanged += Render;
            PresetBox.SelectedIndex = 3; // This month
            ApplyPreset("month");
            _suppress = false;
            await LoadAsync();
        };
        Unloaded += (_, _) =>
        {
            AppSession.LanguageChanged -= Render;
            AppSession.CalendarTypeChanged -= Render;
        };
    }

    private string Res(string key) => (string)FindResource(key);

    // ── Period selection ──────────────────────────────────────────────────

    private void ApplyPreset(string preset)
    {
        var today = DateTime.Today;
        var shamsi = AppSession.CalendarType == CalendarType.Shamsi;
        DateTime from, to = today;
        switch (preset)
        {
            case "today": from = today; break;
            case "yesterday": from = to = today.AddDays(-1); break;
            case "week": from = today.AddDays(-(((int)today.DayOfWeek + 1) % 7)); break; // Saturday start
            case "month":
                from = shamsi
                    ? _persian.ToDateTime(_persian.GetYear(today), _persian.GetMonth(today), 1, 0, 0, 0, 0)
                    : new DateTime(today.Year, today.Month, 1);
                break;
            case "last30": from = today.AddDays(-29); break;
            case "year":
                from = shamsi
                    ? _persian.ToDateTime(_persian.GetYear(today), 1, 1, 0, 0, 0, 0)
                    : new DateTime(today.Year, 1, 1);
                break;
            default: return; // custom: keep what the pickers hold
        }

        var was = _suppress;
        _suppress = true;
        FromPicker.SelectedDate = from;
        ToPicker.SelectedDate = to;
        _suppress = was;
    }

    private async void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || PresetBox.SelectedItem is not ComboBoxItem item) return;
        ApplyPreset((string)item.Tag);
        await LoadAsync();
    }

    private async void DatePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _suppress = true;
        PresetBox.SelectedIndex = 6; // Custom
        _suppress = false;
        await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    // ── Load / render ─────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        if (FromPicker.SelectedDate is not { } from || ToPicker.SelectedDate is not { } to) return;

        var seq = ++_requestSeq;
        try
        {
            var report = await _apiClient.GetStatsReportAsync(DateOnly.FromDateTime(from), DateOnly.FromDateTime(to));
            if (seq != _requestSeq) return;
            _report = report;
            Render();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private void Render()
    {
        if (_report is null) return;
        var r = _report;

        Cards.Children.Clear();
        AddCard("Stats.ExamsTotal", r.TotalExams.ToString());
        AddCard("Stats.Patients", r.DistinctPatients.ToString());
        AddCard("Stats.NewPatients", r.NewPatients.ToString());
        AddCard("Stats.Cancelled", r.CancelledExams.ToString());
        if (r.Money is { } m)
        {
            AddCard("Stats.Billed", Currency.Format(m.Billed));
            AddCard("Stats.NotInvoiced", Currency.Format(m.NotInvoiced));
            AddCard("Stats.Collected", Currency.Format(m.Collected));
            AddCard("Stats.Discounts", Currency.Format(m.Discounts));
            AddCard("Stats.DiscountedExams", m.DiscountedExams.ToString());
            AddCard("Stats.FreeExams", m.FreeExams.ToString());
        }

        TypeGrid.ItemsSource = r.ByExamType.Select(x => new StatRow(x.Label ?? string.Empty, x.Exams, x.Patients, x.Revenue, x.Discount)).ToList();
        ModalityGrid.ItemsSource = r.ByModality.Select(x => new StatRow(x.Label ?? Res("Stats.NoModality"), x.Exams, x.Patients, x.Revenue, x.Discount)).ToList();
        DoctorGrid.ItemsSource = r.ByReferringPhysician.Select(x => new StatRow(x.Label ?? Res("Stats.NoDoctor"), x.Exams, x.Patients, x.Revenue, x.Discount)).ToList();
        DayGrid.ItemsSource = r.ByDay.Select(x => new DayRow(CalendarFormat.Format(x.Date), x.Exams, x.Patients, x.Collected)).ToList();

        CashCard.Visibility = r.CashByUser is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        CashGrid.ItemsSource = r.CashByUser;

        DiscountCard.Visibility = r.DiscountsByUser is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        DiscountGrid.ItemsSource = r.DiscountsByUser;
    }

    private void AddCard(string labelKey, string value) =>
        Cards.Children.Add(new Border
        {
            Style = (Style)FindResource("CardBorder"),
            Width = 235,
            Margin = new Thickness(0, 0, 12, 12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = Res(labelKey), FontSize = 12, FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"), Margin = new Thickness(0, 0, 0, 6),
                    },
                    new TextBlock
                    {
                        Text = value, FontSize = 22, FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    },
                },
            },
        });

    // ── Export ────────────────────────────────────────────────────────────

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var owner = Window.GetWindow(this);
        var name = $"Statistics_{_report.From:yyyy-MM-dd}_{_report.To:yyyy-MM-dd}";
        var path = ExcelExportService.PromptPath(owner, name);
        if (path is null) return;

        try
        {
            ExcelExportService.SaveStatsReport(path, _report);
            ExcelExportService.Open(path);
        }
        catch (IOException)
        {
            MessageBox.Show(owner, Res("Stats.FileInUse"), Res("App.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, Res("App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // A DataGrid that doesn't need to scroll would otherwise swallow the mouse wheel.
    private void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - e.Delta);
    }
}
