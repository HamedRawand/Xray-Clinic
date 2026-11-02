using System.Windows;
using NasimXray.Client.Helpers;
using NasimXray.Client.Services;
using NasimXray.Client.Views;
using Serilog;

namespace NasimXray.Client;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        // The app icon in the title bar of every window and dialog (the taskbar / exe icon comes from <ApplicationIcon>).
        try
        {
            var icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
            Window.IconProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(icon));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the window icon");
        }

        var logPath = System.IO.Path.Combine(
            NasimXray.Shared.AppFolders.Resolve(Environment.SpecialFolder.LocalApplicationData), "Logs", "client_.log");

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}",
                ClinicInfoService.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        ApplyStartupPreferences();
        ClinicInfoService.LoadCache(); // shows the clinic name straight away, even if the server is not reachable yet

        var login = new LoginWindow();
        MainWindow = login;
        login.Show();

        _ = CheckForUpdateInBackgroundAsync();
    }

    private static async Task CheckForUpdateInBackgroundAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null) return;

            Current.Dispatcher.Invoke(() => new UpdateAvailableDialog(update).Show());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
        }
    }

    private static void ApplyStartupPreferences()
    {
        var settings = ClientSettings.Load();

        var language = settings.Language == "Dari" ? AppLanguage.Dari : AppLanguage.English;
        LanguageService.Apply(language);

        AppSession.CalendarType = settings.CalendarType == "Shamsi"
            ? Helpers.CalendarType.Shamsi
            : Helpers.CalendarType.Gregorian;
    }
}
