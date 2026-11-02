using NasimXray.Shared;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Stats;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

// "Today at a glance": icon cards for today's counts, clickable work queues (open that queue) and, for billing users,
// money cards with a payment-method bar; below them the exams by type (with data bars) and by status (with a colour bar).
public partial class DashboardView : UserControl
{
    private enum Tone { Teal, Blue, Green, Amber, Cyan, Red, Gray }

    private readonly ApiClient _apiClient;
    private readonly Action<string> _openQueue;
    private readonly Action? _openBackup;
    private readonly DispatcherTimer _timer;
    private DashboardDto? _dto;
    private DateTime? _loadedAt;
    private DateTime? _lastBackupUtc;      // newest backup file, null = none yet
    private bool _backupKnown;             // false until the backup list has been read successfully
    private bool _backingUp;

    public DashboardView(ApiClient apiClient, Action<string> openQueue, Action? openBackup = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _openQueue = openQueue;
        _openBackup = openBackup;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += async (_, _) => await LoadAsync();

        // Wide windows get more cards per row (fewer scrolls); narrow ones keep the roomier 3 / 2 columns.
        Scroller.SizeChanged += (_, _) =>
        {
            var width = Scroller.ActualWidth - 48;
            QueueCards.Columns = width >= 1000 ? 5 : 3;
            MoneyCards.Columns = width >= 1060 ? 4 : 2;
        };

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
    private Brush Brush_(string key) => (Brush)FindResource(key);

    private async Task LoadAsync()
    {
        try
        {
            _dto = await _apiClient.GetDashboardAsync();
            _loadedAt = DateTime.Now;
            await LoadBackupInfoAsync();
            Render();
        }
        catch (ApiException ex)
        {
            // The 60s auto-refresh shouldn't nag: only the first load (nothing on screen yet) shows the error.
            if (_dto is null) DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    // Only users who may manage backups see (and can read) the backup list; for everyone else the card stays hidden.
    private async Task LoadBackupInfoAsync()
    {
        if (!SessionService.Can(Permission.ManageBackup)) { _backupKnown = false; return; }
        try
        {
            var backups = await _apiClient.GetBackupsAsync();
            _lastBackupUtc = backups.Count == 0 ? null : backups.Max(b => b.CreatedAtUtc);
            _backupKnown = true;
        }
        catch (ApiException)
        {
            _backupKnown = false; // a failed read just hides the card; it must never stop the dashboard from loading
        }
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (_backingUp) return;
        _backingUp = true;
        RenderBackupCard();
        try
        {
            await _apiClient.CreateBackupAsync();
            await LoadBackupInfoAsync();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
        finally
        {
            _backingUp = false;
            RenderBackupCard();
        }
    }

    // ---- the backup reminder card ---------------------------------------------------------------------------
    private void RenderBackupCard()
    {
        if (!SessionService.Can(Permission.ManageBackup) || !_backupKnown)
        {
            BackupCard.Visibility = Visibility.Collapsed;
            return;
        }

        // How many calendar days ago was the newest backup (on this PC's clock)?
        int? days = null;
        if (_lastBackupUtc is { } last)
        {
            var local = DateTime.SpecifyKind(last, DateTimeKind.Utc).ToLocalTime();
            days = Math.Max(0, (DateTime.Now.Date - local.Date).Days);
        }

        // Fresh = made today or yesterday (the nightly job normally covers that); due after 2 days; overdue after a week.
        var (tone, statusKey) = days switch
        {
            null => (Tone.Red, "Dash.Backup.Overdue"),
            <= 1 => (Tone.Green, "Dash.Backup.Ok"),
            <= 7 => (Tone.Amber, "Dash.Backup.Due"),
            _ => (Tone.Red, "Dash.Backup.Overdue"),
        };
        var (bg, fg) = ToneBrushes(tone);

        string when;
        if (days is null)
            when = Res("Dash.Backup.Never");
        else
        {
            var ago = days == 0 ? Res("Dash.Backup.Today") : days == 1 ? Res("Dash.Backup.Yesterday") : string.Format(Res("Dash.Backup.DaysAgo"), days);
            when = $"{CalendarFormat.Format(DateTime.SpecifyKind(_lastBackupUtc!.Value, DateTimeKind.Utc), "datetime")}  ·  {ago}";
        }

        // title row: heading + coloured status pill
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = Res("Dash.Backup.Title"), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush_("TextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new Border
        {
            Background = bg, CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 1, 9, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Res(statusKey), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fg },
        });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 12, 0) };
        text.Children.Add(titleRow);
        text.Children.Add(new TextBlock { Text = when, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brush_("TextPrimaryBrush"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = Res("Dash.Backup.Hint"), FontSize = 12, Foreground = Brush_("TextMutedBrush"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });

        var chip = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Background = bg, VerticalAlignment = VerticalAlignment.Center,
            Child = IconPath(days is <= 1 ? "Icon.CheckCircle" : "Icon.Backup", fg, 22),
        };

        var now = new Button
        {
            Style = (Style)FindResource("PrimaryButton"), Height = 34, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 0, 6),
            Content = Res(_backingUp ? "Dash.Backup.Working" : "Dash.Backup.Now"), IsEnabled = !_backingUp,
        };
        now.Click += BackupNow_Click;
        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
        buttons.Children.Add(now);
        if (_openBackup is not null)
        {
            var open = new Button { Style = (Style)FindResource("SecondaryButton"), Height = 30, Padding = new Thickness(12, 0, 12, 0), FontSize = 12, Content = Res("Dash.Backup.Open") };
            open.Click += (_, _) => _openBackup();
            buttons.Children.Add(open);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(chip);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        BackupCard.Child = grid;
        BackupCard.Visibility = Visibility.Visible;
    }

    private void Render()
    {
        if (_dto is null) return;
        var d = _dto;

        DateText.Text = string.Format(Res("Dash.TodayFormat"), CalendarFormat.Format(d.Date));
        UpdatedText.Text = _loadedAt is { } t ? string.Format(Res("Dash.UpdatedFormat"), t.ToString("HH:mm")) : string.Empty;

        RenderBackupCard();

        // ---- today
        TodayHeading.Text = Res("Dash.SectionToday");
        TodayCards.Children.Clear();
        TodayCards.Children.Add(StatCard("Icon.Exams", Tone.Teal, "Dash.ExamsToday", d.Exams.ToString()));
        TodayCards.Children.Add(StatCard("Icon.Patients", Tone.Blue, "Dash.PatientsToday", d.Patients.ToString()));
        TodayCards.Children.Add(StatCard("Icon.CheckIn", Tone.Green, "Dash.NewPatients", d.NewPatients.ToString()));
        TodayCards.Children.Add(StatCard("Icon.Cancel", Tone.Gray, "Dash.Cancelled", d.Cancelled.ToString()));

        // ---- work queues: a card opens the Exams screen on that queue
        var canSeeQueues = SessionService.Can(Permission.ViewExamOrders);
        QueueSection.Visibility = canSeeQueues ? Visibility.Visible : Visibility.Collapsed;
        QueueHeading.Text = Res("Dash.SectionQueues");
        QueueCards.Children.Clear();
        if (canSeeQueues)
        {
            QueueCards.Children.Add(StatCard("Icon.Hourglass", Tone.Amber, "Exams.Queue.Tech", d.Queues.Pending.ToString(), queue: "tech"));
            QueueCards.Children.Add(StatCard("Icon.Clock", Tone.Cyan, "Exams.Queue.Progress", d.Queues.InProgress.ToString(), queue: "progress"));
            QueueCards.Children.Add(StatCard("Icon.Report", Tone.Blue, "Exams.Queue.Report", d.Queues.Completed.ToString(), queue: "report"));
            if (d.Money is not null)
            {
                QueueCards.Children.Add(StatCard("Icon.Invoices", Tone.Gray, "Exams.Queue.Billing", d.Queues.NotInvoiced.ToString(), queue: "billing"));
                QueueCards.Children.Add(StatCard("Icon.Warning", Tone.Red, "Exams.Queue.Unpaid", d.Queues.Unpaid.ToString(), queue: "unpaid"));
            }
        }

        // ---- money (only for users who may see billing)
        MoneySection.Visibility = d.Money is null ? Visibility.Collapsed : Visibility.Visible;
        MoneyHeading.Text = Res("Dash.SectionMoney");
        MoneyCards.Children.Clear();
        PaymentBar.Children.Clear();
        PaymentBar.ColumnDefinitions.Clear();
        PaymentLegend.Children.Clear();
        if (d.Money is { } m)
        {
            MoneyCards.Children.Add(StatCard("Icon.Payments", Tone.Green, "Dash.Collected", Currency.Format(m.Collected)));
            MoneyCards.Children.Add(StatCard("Icon.Invoices", Tone.Blue, "Dash.Invoiced", Currency.Format(m.Invoiced)));
            MoneyCards.Children.Add(StatCard("Icon.Wallet", Tone.Red, "Dash.Outstanding", Currency.Format(m.Outstanding)));
            MoneyCards.Children.Add(StatCard("Icon.Tag", Tone.Amber, "Dash.Discounts", Currency.Format(m.Discounts),
                sub: m.FreeExams > 0 ? string.Format(Res("Dash.FreeExamsSub"), m.FreeExams) : null));

            PaymentMixCard.Visibility = m.Collected > 0 ? Visibility.Visible : Visibility.Collapsed;
            var mix = new[]
            {
                (Res("PaymentMethod.Cash"), m.Cash, "#16A34A"),
                (Res("PaymentMethod.Card"), m.Card, "#3B82F6"),
                (Res("PaymentMethod.Other"), m.Other, "#94A3B8"),
            };
            SegmentBar(PaymentBar, mix.Select(x => (x.Item3, (double)x.Item2)));
            foreach (var (label, amount, color) in mix.Where(x => x.Item2 > 0))
                PaymentLegend.Children.Add(LegendItem(color, label, Currency.Format(amount)));
        }

        // ---- exams by type, with data bars
        TypeList.Children.Clear();
        NoExamsPanel.Visibility = d.ByExamType.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var max = d.ByExamType.Count == 0 ? 1 : d.ByExamType.Max(x => x.ExamCount);
        foreach (var row in d.ByExamType.OrderByDescending(x => x.ExamCount))
            TypeList.Children.Add(TypeRow(row.ExamTypeName, row.Modality, row.ExamCount, row.PatientCount, max));

        // ---- status colour bar + legend
        StatusList.Children.Clear();
        var total = d.ByStatus.Sum(s => s.Count);
        SegmentBar(StatusBar, d.ByStatus.Select(s => (StatusColor(s.Status), (double)s.Count)));
        foreach (var s in d.ByStatus)
        {
            var pct = total == 0 ? 0 : (int)Math.Round(100.0 * s.Count / total);
            StatusList.Children.Add(LegendRow(StatusColor(s.Status), Res($"Status.{s.Status}"), s.Count.ToString(), $"{pct}%"));
        }
    }

    // ------------------------------------------------------------------------------------------ building blocks

    private (Brush Bg, Brush Fg) ToneBrushes(Tone tone) => tone switch
    {
        Tone.Teal => (Brush_("AccentBrush20"), Brush_("AccentBrush")),
        Tone.Blue => (Brush_("BadgeBlueBg"), Brush_("BadgeBlueFg")),
        Tone.Green => (Brush_("BadgeOkBg"), Brush_("BadgeOkFg")),
        Tone.Amber => (Brush_("BadgeWarnBg"), Brush_("BadgeWarnFg")),
        Tone.Cyan => (Brush_("BadgeInfoBg"), Brush_("BadgeInfoFg")),
        Tone.Red => (Brush_("BadgeDangerBg"), Brush_("BadgeDangerFg")),
        _ => (Brush_("BadgeNeutralBg"), Brush_("BadgeNeutralFg")),
    };

    private Path IconPath(string geometryKey, Brush fill, double size, bool mirrorInRtl = false)
    {
        var path = new Path
        {
            Data = (Geometry)FindResource(geometryKey), Fill = fill, Width = size, Height = size, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        if (mirrorInRtl && FlowDirection == FlowDirection.RightToLeft)
        {
            path.RenderTransformOrigin = new Point(0.5, 0.5);
            path.RenderTransform = new ScaleTransform(-1, 1);
        }
        return path;
    }

    // A card: coloured icon chip, big number, caption (and an optional small line); queue cards also show a chevron and open the queue.
    private Border StatCard(string iconKey, Tone tone, string labelKey, string value, string? sub = null, string? queue = null)
    {
        var (bg, fg) = ToneBrushes(tone);

        var chip = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(11), Background = bg, VerticalAlignment = VerticalAlignment.Center,
            Child = IconPath(iconKey, fg, 20),
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brush_("TextPrimaryBrush") });
        text.Children.Add(new TextBlock
        {
            Text = Res(labelKey), FontSize = 12, Foreground = Brush_("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap,
        });
        if (sub is not null)
            text.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Brush_("TextMutedBrush"), Margin = new Thickness(0, 2, 0, 0) });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(chip);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var card = new Border
        {
            Style = (Style)FindResource(queue is null ? "CardBorder" : "DashCardClickable"),
            Padding = new Thickness(14, 12, 12, 12),
            Margin = new Thickness(0, 0, 10, 10),
            Child = grid,
        };

        if (queue is not null)
        {
            var chevron = IconPath("Icon.Chevron", Brush_("TextMutedBrush"), 18, mirrorInRtl: true);
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);
            card.MouseLeftButtonUp += (_, _) => _openQueue(queue);
        }
        return card;
    }

    // One horizontal bar made of coloured segments, proportional to the values (a thin grey track when everything is zero).
    private static void SegmentBar(Grid host, IEnumerable<(string Color, double Value)> parts)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();

        var visible = parts.Where(p => p.Value > 0).ToList();
        if (visible.Count == 0)
        {
            host.Children.Add(new Border { Background = (Brush)new BrushConverter().ConvertFrom("#E2E8F0")!, CornerRadius = new CornerRadius(6) });
            return;
        }

        for (var i = 0; i < visible.Count; i++)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(visible[i].Value, GridUnitType.Star) });
            var first = i == 0;
            var last = i == visible.Count - 1;
            var segment = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFrom(visible[i].Color)!,
                CornerRadius = new CornerRadius(first ? 6 : 1, last ? 6 : 1, last ? 6 : 1, first ? 6 : 1),
                Margin = new Thickness(0, 0, last ? 0 : 2, 0),
            };
            Grid.SetColumn(segment, i);
            host.Children.Add(segment);
        }
    }

    private FrameworkElement LegendItem(string color, string label, string value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 4) };
        panel.Children.Add(Dot(color));
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush_("TextSecondaryBrush"), Margin = new Thickness(7, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush_("TextPrimaryBrush") });
        return panel;
    }

    private FrameworkElement LegendRow(string color, string label, string count, string percent)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Dot(color));
        var name = new TextBlock { Text = label, FontSize = 13, Foreground = Brush_("TextSecondaryBrush"), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var numbers = new StackPanel { Orientation = Orientation.Horizontal };
        numbers.Children.Add(new TextBlock { Text = count, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush_("TextPrimaryBrush") });
        numbers.Children.Add(new TextBlock { Text = percent, FontSize = 11, Foreground = Brush_("TextMutedBrush"), Margin = new Thickness(8, 1, 0, 0), MinWidth = 30, TextAlignment = TextAlignment.Right });
        Grid.SetColumn(numbers, 2);
        grid.Children.Add(numbers);
        return grid;
    }

    private static Ellipse Dot(string color) => new()
    {
        Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center,
        Fill = (Brush)new BrushConverter().ConvertFrom(color)!,
    };

    private static string StatusColor(ExamOrderStatus status) => status switch
    {
        ExamOrderStatus.Pending => "#F59E0B",
        ExamOrderStatus.InProgress => "#0891B2",
        ExamOrderStatus.Completed => "#3B82F6",
        ExamOrderStatus.Reported => "#16A34A",
        _ => "#94A3B8",
    };

    private FrameworkElement TypeRow(string name, string? modality, int exams, int patients, int max)
    {
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock
        {
            Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush_("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(modality))
            left.Children.Add(new Border
            {
                Background = Brush_("BadgeNeutralBg"), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = modality, FontSize = 11, Foreground = Brush_("BadgeNeutralFg") },
            });
        top.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(new TextBlock { Text = exams.ToString(), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brush_("TextPrimaryBrush") });
        right.Children.Add(new TextBlock
        {
            Text = string.Format(Res("Dash.PatientsFormat"), patients), FontSize = 11, Foreground = Brush_("TextMutedBrush"),
            Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(right, 1);
        top.Children.Add(right);

        var bar = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 0) };
        bar.Children.Add(new Border { Background = Brush_("TableHeaderBrush"), CornerRadius = new CornerRadius(3) });
        var fill = new Grid();
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(exams, 0), GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(max - exams, 0.0001), GridUnitType.Star) });
        fill.Children.Add(new Border { Background = Brush_("AccentBrush"), CornerRadius = new CornerRadius(3) });
        bar.Children.Add(fill);

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 13) };
        row.Children.Add(top);
        row.Children.Add(bar);
        return row;
    }
}
