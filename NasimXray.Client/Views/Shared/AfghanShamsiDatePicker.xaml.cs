using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NasimXray.Client.Views.Shared;

public partial class AfghanShamsiDatePicker : UserControl
{
    private static readonly PersianCalendar _pc = new();

    private static readonly string[] _monthNames =
    {
        "", "حمل", "ثور", "جوزا", "سرطان", "اسد", "سنبله",
        "میزان", "عقرب", "قوس", "جدی", "دلو", "حوت",
    };

    private static readonly string[] _dayAbbrev = { "ش", "ی", "د", "س", "چ", "پ", "ج" };

    private static readonly SolidColorBrush _accentBrush = new(Color.FromRgb(0x0E, 0x74, 0x90));
    private static readonly SolidColorBrush _whiteBrush = new(Colors.White);
    private static readonly SolidColorBrush _normalFg = new(Color.FromRgb(0x0F, 0x17, 0x2A));
    private static readonly SolidColorBrush _transparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush _todayBorder = new(Color.FromRgb(0x0E, 0x74, 0x90));
    private static readonly SolidColorBrush _mutedFg = new(Color.FromRgb(0x94, 0xA3, 0xB8));

    private int _displayYear;
    private int _displayMonth;
    private bool _headerBuilt;

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate), typeof(DateTime?), typeof(AfghanShamsiDatePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault |
                FrameworkPropertyMetadataOptions.Journal,
                OnSelectedDateChanged));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((AfghanShamsiDatePicker)d).RefreshDisplay((DateTime?)e.NewValue);

    public AfghanShamsiDatePicker()
    {
        InitializeComponent();
        var today = DateTime.Today;
        _displayYear = _pc.GetYear(today);
        _displayMonth = _pc.GetMonth(today);
    }

    private void RefreshDisplay(DateTime? date)
    {
        if (date.HasValue)
        {
            int y = _pc.GetYear(date.Value), m = _pc.GetMonth(date.Value), d = _pc.GetDayOfMonth(date.Value);
            DateTextBlock.Text = $"{y:0000}/{m:00}/{d:00}";
            _displayYear = y;
            _displayMonth = m;
        }
        else
        {
            DateTextBlock.Text = string.Empty;
        }
    }

    private void CalendarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDate.HasValue)
        {
            _displayYear = _pc.GetYear(SelectedDate.Value);
            _displayMonth = _pc.GetMonth(SelectedDate.Value);
        }
        else
        {
            var t = DateTime.Today;
            _displayYear = _pc.GetYear(t);
            _displayMonth = _pc.GetMonth(t);
        }
        EnsureDayHeaders();
        PopulateCalendar();
        CalendarPopup.IsOpen = true;
    }

    // Focusable="False" + e.Handled + BeginInvoke re-open are all required for these nav buttons
    // to work when this control is hosted inside a DataGrid cell popup (see the skill's known
    // issues table — DataGrid focus management otherwise closes StaysOpen="False" popups).
    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_displayMonth == 1) { _displayMonth = 12; _displayYear--; }
        else _displayMonth--;
        PopulateCalendar();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            () => CalendarPopup.IsOpen = true);
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_displayMonth == 12) { _displayMonth = 1; _displayYear++; }
        else _displayMonth++;
        PopulateCalendar();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            () => CalendarPopup.IsOpen = true);
    }

    private void EnsureDayHeaders()
    {
        if (_headerBuilt) return;
        _headerBuilt = true;
        DayNamesGrid.Children.Clear();
        foreach (var a in _dayAbbrev)
        {
            DayNamesGrid.Children.Add(new TextBlock
            {
                Text = a,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                Foreground = _mutedFg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
        }
    }

    private void PopulateCalendar()
    {
        MonthYearLabel.Text = $"{_monthNames[_displayMonth]}  {_displayYear}";
        DaysGrid.Children.Clear();

        var firstOfMonth = _pc.ToDateTime(_displayYear, _displayMonth, 1, 0, 0, 0, 0);
        var daysInMonth = _pc.GetDaysInMonth(_displayYear, _displayMonth);
        var today = DateTime.Today;

        // Saturday=6→col0, Sunday=0→col1, …, Friday=5→col6
        var firstCol = ((int)firstOfMonth.DayOfWeek + 1) % 7;

        for (var cell = 0; cell < 42; cell++)
        {
            var dayNum = cell - firstCol + 1;
            if (dayNum < 1 || dayNum > daysInMonth)
            {
                DaysGrid.Children.Add(new Border());
                continue;
            }

            var cellDate = _pc.ToDateTime(_displayYear, _displayMonth, dayNum, 0, 0, 0, 0);
            var isSelected = SelectedDate.HasValue && SelectedDate.Value.Date == cellDate.Date;
            var isToday = cellDate.Date == today.Date;

            var btn = new Button
            {
                Content = dayNum.ToString(),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                Width = 34,
                Height = 34,
                Margin = new Thickness(1),
                Cursor = Cursors.Hand,
                BorderThickness = isToday && !isSelected ? new Thickness(1.5) : new Thickness(0),
                BorderBrush = isToday && !isSelected ? _todayBorder : null,
                Background = isSelected ? _accentBrush : _transparentBrush,
                Foreground = isSelected ? _whiteBrush : _normalFg,
                Tag = isSelected ? "selected" : "normal",
                Template = (ControlTemplate)Resources["DayBtnTemplate"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
            };

            var captured = cellDate;
            btn.Click += (_, _) => { SelectedDate = captured; CalendarPopup.IsOpen = false; };
            DaysGrid.Children.Add(btn);
        }
    }
}
