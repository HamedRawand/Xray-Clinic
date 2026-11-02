using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NasimXray.Client.Views;

namespace NasimXray.Client.Services;

// Locks the whole app (every open window, not just MainWindow) after a period of no mouse/
// keyboard activity — matches OPD Clinic's proven pattern of disabling every Application.Current
// window rather than just the main one, since a receptionist could easily leave a modal dialog
// (PatientDetailWindow, ReportEditWindow) open and walk away.
public static class IdleLockService
{
    private static readonly TimeSpan DefaultThreshold = TimeSpan.FromMinutes(10);
    private static TimeSpan _threshold = DefaultThreshold;
    private static bool _handlersRegistered;
    private static DateTime _lastActivity = DateTime.UtcNow;
    private static DispatcherTimer? _timer;
    private static bool _locked;
    private static ApiClient? _apiClient;

    public static event Action? SignOutRequested;

    // threshold override is for testing only — production callers should omit it and get the
    // default 10-minute timeout.
    public static void Start(ApiClient apiClient, TimeSpan? threshold = null)
    {
        _apiClient = apiClient;
        _threshold = threshold ?? DefaultThreshold;
        _lastActivity = DateTime.UtcNow;
        _locked = false;

        if (!_handlersRegistered)
        {
            _handlersRegistered = true;
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => RecordActivity()));
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseMoveEvent, new MouseEventHandler((_, _) => RecordActivity()));
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, _) => RecordActivity()));
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => CheckIdle();
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _apiClient = null;
    }

    private static void RecordActivity() => _lastActivity = DateTime.UtcNow;

    private static void CheckIdle()
    {
        if (_locked || _apiClient is null) return;
        if (DateTime.UtcNow - _lastActivity < _threshold) return;

        _locked = true;
        LockNow();
    }

    private static void LockNow()
    {
        var windowsToReEnable = Application.Current.Windows.Cast<Window>().Where(w => w.IsEnabled).ToList();
        foreach (var w in windowsToReEnable) w.IsEnabled = false;

        var lockWindow = new IdleLockWindow(_apiClient!);

        lockWindow.Unlocked += () =>
        {
            _locked = false;
            _lastActivity = DateTime.UtcNow;
            foreach (var w in windowsToReEnable) w.IsEnabled = true;
        };

        lockWindow.SignOutRequested += () =>
        {
            foreach (var w in windowsToReEnable) w.IsEnabled = true;
            SignOutRequested?.Invoke();
        };

        lockWindow.Show();
    }
}
