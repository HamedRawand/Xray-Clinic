using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

// Every logged action, newest first, paged. Search/action/date filters apply across the whole history.
public partial class AuditLogView : UserControl
{
    private const int PageSize = 50;

    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _searchDebounce;
    private List<string> _actions = [];
    private int _page = 1;
    private int _requestSeq;
    private bool _suppress = true;

    public AuditLogView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); _page = 1; await LoadAsync(); };

        Pager.PageRequested += async page => { _page = page; await LoadAsync(); };

        Loaded += async (_, _) =>
        {
            AppSession.LanguageChanged += RebuildActions;
            await InitAsync();
        };
        Unloaded += (_, _) => AppSession.LanguageChanged -= RebuildActions;
    }

    private string Res(string key) => (string)FindResource(key);

    private async Task InitAsync()
    {
        try { _actions = await _apiClient.GetAuditActionsAsync(); }
        catch (ApiException) { /* the filter just offers "All actions" */ }

        RebuildActions();
        _suppress = false;
        await LoadAsync();
    }

    // "All actions" + every logged action; keeps the selection when the language changes.
    private void RebuildActions()
    {
        var was = _suppress;
        _suppress = true;
        var selected = (ActionBox.SelectedItem as ComboOption)?.Value as string ?? string.Empty;
        var options = new List<ComboOption> { new(Res("AuditLog.AllActions"), string.Empty) };
        options.AddRange(_actions.Select(a => new ComboOption(a, a)));
        ActionBox.ItemsSource = options;
        ActionBox.SelectedItem = options.FirstOrDefault(o => (string)o.Value == selected) ?? options[0];
        _suppress = was;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _page = 1;
        await LoadAsync();
    }

    private async void DatePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _page = 1;
        await LoadAsync();
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        _suppress = true;
        SearchBox.Clear();
        ActionBox.SelectedIndex = 0;
        FromPicker.SelectedDate = null;
        ToPicker.SelectedDate = null;
        _suppress = false;
        _page = 1;
        await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        var seq = ++_requestSeq;
        try
        {
            var action = (ActionBox.SelectedItem as ComboOption)?.Value as string;
            var result = await _apiClient.GetAuditLogsAsync(
                search: SearchBox.Text,
                action: string.IsNullOrEmpty(action) ? null : action,
                dateFrom: FromPicker.SelectedDate is { } f ? DateOnly.FromDateTime(f) : null,
                dateTo: ToPicker.SelectedDate is { } t ? DateOnly.FromDateTime(t) : null,
                page: _page,
                pageSize: PageSize);
            if (seq != _requestSeq) return;

            _page = result.Page;
            Grid.ItemsSource = result.Items;
            EmptyText.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Pager.Update(result);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }
}
