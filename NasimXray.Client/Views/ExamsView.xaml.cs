using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

public partial class ExamsView : UserControl
{
    private sealed record FilterOption(string Label, object? Value)
    {
        public override string ToString() => Label;
    }

    private const int PageSize = 50;

    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _debounce;
    private readonly PersianCalendar _persian = new();

    private List<(int Id, string Name)> _examTypes = [];
    private List<string> _modalities = [];
    private List<(int Id, string Name)> _physicians = [];

    private readonly DispatcherTimer _countsTimer;
    private string? _activeQueue;
    private ExamQueueCountsDto? _counts;

    private int _page = 1;
    private int _totalPages = 1;
    private int _requestSeq;
    private bool _suppress = true;

    private readonly string? _initialQueue;

    public ExamsView(ApiClient apiClient, string? initialQueue = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _initialQueue = initialQueue;
        UpdateQueueButtons(); // styled immediately; counts fill in once loaded

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await LoadAsync(); };

        // Queue sizes stay current while the screen is open (someone else may add or finish exams).
        _countsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _countsTimer.Tick += async (_, _) => await RefreshCountsAsync();

        // Buttons the user's role can't use are hidden rather than left to fail with "forbidden".
        WorkflowColumn.Visibility = SessionService.Can(Permission.ManageExamOrders) ? Visibility.Visible : Visibility.Collapsed;

        Loaded += async (_, _) =>
        {
            AppSession.LanguageChanged += OnLanguageChanged;
            _countsTimer.Start();
            await InitAsync();
        };
        Unloaded += (_, _) =>
        {
            AppSession.LanguageChanged -= OnLanguageChanged;
            _countsTimer.Stop();
        };
    }

    private void OnLanguageChanged()
    {
        RebuildLookups();
        UpdateQueueButtons();
    }

    private async Task InitAsync()
    {
        // Lookups use includeInactive so exams for since-retired types/doctors stay searchable.
        try
        {
            var types = await _apiClient.GetExamTypesAsync(includeInactive: true);
            _examTypes = types.OrderBy(t => t.Name).Select(t => (t.Id, t.Name)).ToList();
            _modalities = types.Select(t => t.Modality).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!)
                .Distinct().OrderBy(m => m).ToList();
        }
        catch (ApiException) { /* filter stays on "All" */ }

        try { _physicians = (await _apiClient.GetReferringPhysiciansAsync(includeInactive: true)).OrderBy(p => p.Name).Select(p => (p.Id, p.Name)).ToList(); }
        catch (ApiException) { /* filter stays on "All" */ }

        RebuildLookups();

        PresetBox.SelectedIndex = 0; // Today
        ApplyPreset("today");
        _suppress = false;
        UpdateQueueButtons();

        // Open on the queue that matches the user's job: radiologists land on reports waiting for them,
        // technicians on exams waiting to be done; reception/admin start with today's exams.
        var defaultQueue = _initialQueue ?? DefaultQueue();
        if (defaultQueue is not null)
            await ApplyQueueAsync(defaultQueue);
        else
            await LoadAsync();
    }

    public static bool HasRoleQueue => DefaultQueue() is not null;

    private static string? DefaultQueue()
    {
        if (SessionService.Can(Permission.ManageBilling)) return null;
        if (SessionService.Can(Permission.FinalizeReport)) return "report";
        if (SessionService.Can(Permission.ManageExamOrders) && !SessionService.Can(Permission.CreateReport)) return "tech";
        return null;
    }

    private string Res(string key) => (string)FindResource(key);

    private void RebuildLookups()
    {
        var wasSuppressed = _suppress;
        _suppress = true;

        Fill(ExamTypeBox, _examTypes.Select(t => new FilterOption(t.Name, t.Id)));
        Fill(ModalityBox, _modalities.Select(m => new FilterOption(m, m)));
        Fill(PhysicianBox, _physicians.Select(p => new FilterOption(p.Name, p.Id)));

        _suppress = wasSuppressed;
    }

    // Rebuilds a combo as "All" + options, keeping the current selection (the "All" label follows the language).
    private void Fill(ComboBox box, IEnumerable<FilterOption> options)
    {
        var selected = (box.SelectedItem as FilterOption)?.Value;
        var list = new List<FilterOption> { new(Res("Exams.All"), null) };
        list.AddRange(options);
        box.ItemsSource = list;
        box.SelectedItem = list.FirstOrDefault(o => Equals(o.Value, selected)) ?? list[0];
    }

    private void ApplyPreset(string preset)
    {
        var today = DateTime.Today;
        DateTime? from, to;
        switch (preset)
        {
            case "today": from = to = today; break;
            case "yesterday": from = to = today.AddDays(-1); break;
            case "week": // Afghan week starts on Saturday
                from = today.AddDays(-(((int)today.DayOfWeek + 1) % 7)); to = today; break;
            case "month":
                from = AppSession.CalendarType == CalendarType.Shamsi
                    ? _persian.ToDateTime(_persian.GetYear(today), _persian.GetMonth(today), 1, 0, 0, 0, 0)
                    : new DateTime(today.Year, today.Month, 1);
                to = today; break;
            case "all": from = to = null; break;
            default: return; // "custom" keeps whatever the pickers hold
        }

        var wasSuppressed = _suppress;
        _suppress = true;
        FromPicker.SelectedDate = from;
        ToPicker.SelectedDate = to;
        _suppress = wasSuppressed;
    }

    private void Schedule()
    {
        if (_suppress) return;
        _activeQueue = null; // any manual filter change leaves queue mode
        UpdateQueueButtons();
        _page = 1;
        _debounce.Stop();
        _debounce.Start();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Schedule();

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => Schedule();

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || PresetBox.SelectedItem is not ComboBoxItem item) return;
        ApplyPreset((string)item.Tag);
        Schedule();
    }

    private void DatePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        // Editing a date by hand turns the preset into "Custom".
        _suppress = true;
        PresetBox.SelectedIndex = 5;
        _suppress = false;
        Schedule();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _suppress = true;
        SearchBox.Text = string.Empty;
        PresetBox.SelectedIndex = 0;
        ApplyPreset("today");
        ExamTypeBox.SelectedIndex = 0;
        ModalityBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = 0;
        PhysicianBox.SelectedIndex = 0;
        BillingBox.SelectedIndex = 0;
        _suppress = false;
        Schedule();
    }

    private ExamOrderSearchQuery BuildQuery()
    {
        var q = new ExamOrderSearchQuery
        {
            Search = SearchBox.Text,
            DateFrom = FromPicker.SelectedDate is { } f ? DateOnly.FromDateTime(f) : null,
            DateTo = ToPicker.SelectedDate is { } t ? DateOnly.FromDateTime(t) : null,
            Page = _page,
            PageSize = PageSize,
        };
        if ((ExamTypeBox.SelectedItem as FilterOption)?.Value is int typeId) q.ExamTypeId = typeId;
        if ((ModalityBox.SelectedItem as FilterOption)?.Value is string modality) q.Modality = modality;
        if (StatusBox.SelectedItem is ComboBoxItem { Tag: string status }) q.Status = Enum.Parse<ExamOrderStatus>(status);
        if ((PhysicianBox.SelectedItem as FilterOption)?.Value is int doctorId) q.ReferringPhysicianId = doctorId;
        if (BillingBox.SelectedItem is ComboBoxItem { Tag: string billing }) q.Billing = Enum.Parse<ExamBillingFilter>(billing);
        return q;
    }

    private async Task LoadAsync()
    {
        var seq = ++_requestSeq;
        try
        {
            var response = await _apiClient.SearchExamOrdersAsync(BuildQuery());
            if (seq != _requestSeq) return; // a newer search is already in flight

            Grid.ItemsSource = response.Items;
            TypeChips.ItemsSource = response.ByExamType;
            EmptyText.Visibility = response.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            SummaryText.Text = string.Format(Res("Exams.SummaryFormat"), response.TotalCount, response.DistinctPatients);

            _totalPages = Math.Max(1, (int)Math.Ceiling(response.TotalCount / (double)PageSize));
            PageText.Text = string.Format(Res("Exams.PageFormat"), response.Page, _totalPages);
            PrevButton.IsEnabled = response.Page > 1;
            NextButton.IsEnabled = response.Page < _totalPages;

            _ = RefreshCountsAsync();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 1) return;
        _page--;
        await LoadAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page >= _totalPages) return;
        _page++;
        await LoadAsync();
    }

    // Clicking a per-type count narrows the register to that exam type.
    private void TypeChip_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExamTypeCountDto chip) return;
        if (ExamTypeBox.ItemsSource is IEnumerable<FilterOption> options &&
            options.FirstOrDefault(o => o.Value is int id && id == chip.ExamTypeId) is { } match)
            ExamTypeBox.SelectedItem = match;
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on header/empty space and on the row buttons.
        if (e.OriginalSource is FrameworkElement source && FindParent<Button>(source) is not null) return;
        if (Grid.SelectedItem is ExamOrderDto order)
            OpenPatientDetail(order);
    }




    // Row "..." menu: built per row from the exam's state and what the signed-in user may do.
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ExamOrderDto order) return;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        void Add(string key, Func<Task> action, bool danger = false)
        {
            var item = new MenuItem { Header = Res(key) };
            if (danger) item.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        if (SessionService.Can(Permission.ViewReports)) Add("PatientDetail.OpenReport", () => OpenReportAsync(order));
        if (SessionService.Can(Permission.ViewPatients)) Add("Exams.OpenPatient", () => { OpenPatientDetail(order); return Task.CompletedTask; });
        if (order.IsDiscountable && SessionService.Can(Permission.ApplyDiscount)) Add("Exams.Discount", () => DiscountAsync(order));
        if (SessionService.Can(Permission.ManageExamOrders) && order.Status is ExamOrderStatus.Pending or ExamOrderStatus.InProgress)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            Add("Exams.Cancel", () => CancelExamAsync(order), danger: true);
        }

        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    private async Task OpenReportAsync(ExamOrderDto order)
    {
        var window = new ReportEditWindow(_apiClient, order) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        await LoadAsync();
    }

    private async Task DiscountAsync(ExamOrderDto order)
    {
        var dialog = new ExamDiscountDialog(_apiClient, order) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private async Task CancelExamAsync(ExamOrderDto order)
    {
        var dialog = new CancelExamDialog($"{order.ExamNumber} · {order.PatientName} · {order.ExamTypeName}") { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await ChangeStatusAsync(order, ExamOrderStatus.Cancelled, dialog.Reason);
    }

    private async void OpenPatientDetail(ExamOrderDto order)
    {
        var window = new PatientDetailWindow(_apiClient, order.PatientId) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        await LoadAsync();
    }

    // ── Work queues ───────────────────────────────────────────────────────

    private async void Queue_Click(object sender, RoutedEventArgs e)
    {
        var queue = (string)((FrameworkElement)sender).Tag;
        if (_activeQueue == queue)
        {
            Clear_Click(sender, e); // clicking the active queue again leaves queue mode
            return;
        }
        await ApplyQueueAsync(queue);
    }

    // Queues look at ALL time: an unreported exam from last week is still waiting.
    private async Task ApplyQueueAsync(string queue)
    {
        _suppress = true;
        SearchBox.Text = string.Empty;
        PresetBox.SelectedIndex = 4; // All time
        ApplyPreset("all");
        ExamTypeBox.SelectedIndex = 0;
        ModalityBox.SelectedIndex = 0;
        PhysicianBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = queue switch { "tech" => 1, "progress" => 2, "report" => 3, _ => 0 };
        BillingBox.SelectedIndex = queue switch { "billing" => 1, "unpaid" => 2, _ => 0 };
        _suppress = false;

        _activeQueue = queue;
        _page = 1;
        UpdateQueueButtons();
        await LoadAsync();
    }

    private void UpdateQueueButtons()
    {
        void Set(Button button, string key, string queue, int? count)
        {
            var label = Res(key);
            button.Content = count.HasValue ? $"{label} ({count.Value})" : label;
            button.Style = (Style)FindResource(_activeQueue == queue ? "PrimaryButton" : "SecondaryButton");
            button.Height = 32;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.FontSize = 12;
        }

        Set(QueueTech, "Exams.Queue.Tech", "tech", _counts?.Pending);
        Set(QueueProgress, "Exams.Queue.Progress", "progress", _counts?.InProgress);
        Set(QueueReport, "Exams.Queue.Report", "report", _counts?.Completed);
        Set(QueueBilling, "Exams.Queue.Billing", "billing", _counts?.NotInvoiced);
        Set(QueueUnpaid, "Exams.Queue.Unpaid", "unpaid", _counts?.Unpaid);
    }

    private async Task RefreshCountsAsync()
    {
        try
        {
            _counts = await _apiClient.GetExamQueueCountsAsync();
            UpdateQueueButtons();
        }
        catch (ApiException)
        {
            // Counts are a convenience; keep the last known numbers and let the next tick retry.
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    // Exports every exam matching the current filters (not just the visible page).
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var path = ExcelExportService.PromptPath(owner, $"Exams_{DateTime.Today:yyyy-MM-dd}");
        if (path is null) return;

        try
        {
            var rows = await _apiClient.ExportExamOrdersAsync(BuildQuery());
            if (rows.Count == 0)
            {
                MessageBox.Show(owner, Res("Exams.ExportEmpty"), Res("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ExcelExportService.SaveExamRegister(path, rows);
            ExcelExportService.Open(path);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(owner, ex);
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

    // ── Status workflow ───────────────────────────────────────────────────

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ExamOrderDto order)
            await ChangeStatusAsync(order, ExamOrderStatus.InProgress);
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ExamOrderDto order)
            await ChangeStatusAsync(order, ExamOrderStatus.Completed);
    }

    private async void Reopen_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExamOrderDto order) return;
        var owner = Window.GetWindow(this);
        if (MessageBox.Show(owner, Res("Exams.ConfirmReopen"), Res("App.Title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await ChangeStatusAsync(order, ExamOrderStatus.InProgress);
    }


    private async Task ChangeStatusAsync(ExamOrderDto order, ExamOrderStatus status, string? cancelReason = null)
    {
        try
        {
            await _apiClient.UpdateExamOrderStatusAsync(order.Id, new ExamOrderStatusUpdateRequest
            {
                Status = status,
                RowVersion = order.RowVersion,
                CancelReason = cancelReason,
            });
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }

        // Reload either way: on success the row moves, on a conflict it shows what someone else did.
        await LoadAsync();
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
