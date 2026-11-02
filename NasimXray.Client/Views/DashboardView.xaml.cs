using NasimXray.Shared;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Stats;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

// "Today at a glance": counts, queue sizes (click one to open that queue) and, for billing users, money.
public partial class DashboardView : UserControl
{
    private readonly ApiClient _apiClient;
    private readonly Action<string> _openQueue;
    private readonly DispatcherTimer _timer;
    private DashboardDto? _dto;
    private DateTime? _loadedAt;

    public DashboardView(ApiClient apiClient, Action<string> openQueue)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _openQueue = openQueue;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += async (_, _) => await LoadAsync();

        Loaded += async (_, _) =>
        {
            AppSession.LanguageChanged += Render;
            AppSession.CalendarTypeChanged += Render;
            _timer.Start();
            await LoadAsync();
        };
        Unloaded += (_, _) =>
        {
            AppSession.LanguageChanged -= Render;
            AppSession.CalendarTypeChanged -= Render;
            _timer.Stop();
        };
    }

    private string Res(string key) => (string)FindResource(key);

    private async Task LoadAsync()
    {
        try
        {
            _dto = await _apiClient.GetDashboardAsync();
            _loadedAt = DateTime.Now;
            Render();
        }
        catch (ApiException ex)
        {
            // The 60s auto-refresh shouldn't nag: only the first load (nothing on screen yet) shows the error.
            if (_dto is null) DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Render()
    {
        if (_dto is null) return;
        var d = _dto;

        DateText.Text = string.Format(Res("Dash.TodayFormat"), CalendarFormat.Format(d.Date));
        UpdatedText.Text = _loadedAt is { } t ? string.Format(Res("Dash.UpdatedFormat"), t.ToString("HH:mm")) : string.Empty;

        Cards.Children.Clear();
        AddCard("Dash.ExamsToday", d.Exams.ToString(), null);
        AddCard("Dash.PatientsToday", d.Patients.ToString(), null);
        AddCard("Dash.NewPatients", d.NewPatients.ToString(), null);

        // Queue cards open the Exams screen on that queue.
        if (SessionService.Can(Permission.ViewExamOrders))
        {
            AddCard("Exams.Queue.Tech", d.Queues.Pending.ToString(), "tech");
            AddCard("Exams.Queue.Progress", d.Queues.InProgress.ToString(), "progress");
            AddCard("Exams.Queue.Report", d.Queues.Completed.ToString(), "report");
            if (d.Money is not null)
            {
                AddCard("Exams.Queue.Billing", d.Queues.NotInvoiced.ToString(), "billing");
                AddCard("Exams.Queue.Unpaid", d.Queues.Unpaid.ToString(), "unpaid");
            }
        }

        if (d.Money is { } m)
        {
            AddCard("Dash.Collected", Currency.Format(m.Collected), null);
            AddCard("Dash.Outstanding", Currency.Format(m.Outstanding), null);
            AddCard("Dash.Discounts", Currency.Format(m.Discounts), null);
            AddCard("Dash.FreeExams", m.FreeExams.ToString(), null);
        }

        TypeGrid.ItemsSource = d.ByExamType;
        NoExamsText.Visibility = d.ByExamType.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        StatusList.Children.Clear();
        foreach (var s in d.ByStatus)
            StatusList.Children.Add(Row(Res($"Status.{s.Status}"), s.Count.ToString()));

        MoneyCard.Visibility = d.Money is null ? Visibility.Collapsed : Visibility.Visible;
        MoneyList.Children.Clear();
        if (d.Money is { } money)
        {
            MoneyList.Children.Add(Row(Res("Dash.Invoiced"), Currency.Format(money.Invoiced)));
            MoneyList.Children.Add(Row(Res("PaymentMethod.Cash"), Currency.Format(money.Cash)));
            MoneyList.Children.Add(Row(Res("PaymentMethod.Card"), Currency.Format(money.Card)));
            MoneyList.Children.Add(Row(Res("PaymentMethod.Other"), Currency.Format(money.Other)));
            MoneyList.Children.Add(Row(Res("Dash.Collected"), Currency.Format(money.Collected), bold: true));
        }
    }

    private void AddCard(string labelKey, string value, string? queue)
    {
        var card = new Border
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
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = value, FontSize = 22, FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource(queue is null ? "TextPrimaryBrush" : "AccentBrush"),
                    },
                },
            },
        };

        if (queue is not null)
        {
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => _openQueue(queue);
        }
        Cards.Children.Add(card);
    }

    private Grid Row(string label, string value, bool bold = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new TextBlock
        {
            Text = label, FontSize = 13,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        };
        var right = new TextBlock
        {
            Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    // A DataGrid that doesn't need to scroll would otherwise swallow the mouse wheel.
    private void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - e.Delta);
    }
}
