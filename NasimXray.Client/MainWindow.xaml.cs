using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NasimXray.Client.Services;
using NasimXray.Client.Views;
using NasimXray.Shared.Enums;

namespace NasimXray.Client;

public partial class MainWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _clinicTimer;

    public MainWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        var user = SessionService.Current;
        UserText.Text = user is null ? string.Empty : $"{user.FullName} ({user.RoleDisplayName})";

        NavDashboard.Visibility = SessionService.Can(Permission.ViewExamOrders) ? Visibility.Visible : Visibility.Collapsed;
        NavStatistics.Visibility = SessionService.Can(Permission.ViewExamOrders) ? Visibility.Visible : Visibility.Collapsed;
        NavCheckIn.Visibility = SessionService.Can(Permission.ManageExamOrders) && SessionService.Can(Permission.ViewPatients)
            ? Visibility.Visible : Visibility.Collapsed;
        NavExams.Visibility = SessionService.Can(Permission.ViewExamOrders) ? Visibility.Visible : Visibility.Collapsed;
        NavPatients.Visibility = SessionService.Can(Permission.ViewPatients) ? Visibility.Visible : Visibility.Collapsed;
        NavInvoices.Visibility = SessionService.Can(Permission.ViewBilling) ? Visibility.Visible : Visibility.Collapsed;
        NavExamTypes.Visibility = SessionService.Can(Permission.ManageExamTypes) ? Visibility.Visible : Visibility.Collapsed;
        NavReferringPhysicians.Visibility = SessionService.Can(Permission.ManageReferringPhysicians) ? Visibility.Visible : Visibility.Collapsed;
        NavUsers.Visibility = SessionService.Can(Permission.ManageUsers) ? Visibility.Visible : Visibility.Collapsed;
        NavCustomRoles.Visibility = SessionService.Can(Permission.ManageRoles) ? Visibility.Visible : Visibility.Collapsed;
        NavAuditLog.Visibility = SessionService.Can(Permission.ViewAuditLog) ? Visibility.Visible : Visibility.Collapsed;
        NavBackup.Visibility = SessionService.Can(Permission.ManageBackup) ? Visibility.Visible : Visibility.Collapsed;

        NavClinicSettings.Visibility = SessionService.Can(Permission.ManageClinicSettings) ? Visibility.Visible : Visibility.Collapsed;

        // Clinic identity: keep the name/logo current (other PCs may change it), and nudge an admin to set it up once.
        ClinicInfoService.Changed += UpdateSetupBanner;
        UpdateSetupBanner();
        _ = ClinicInfoService.RefreshAsync(_apiClient);
        _clinicTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _clinicTimer.Tick += async (_, _) => await ClinicInfoService.RefreshAsync(_apiClient);
        _clinicTimer.Start();
        Closed += (_, _) =>
        {
            ClinicInfoService.Changed -= UpdateSetupBanner;
            _clinicTimer.Stop();
        };

        _apiClient.SessionExpired += ApiClient_SessionExpired;

        IdleLockService.Start(_apiClient);
        IdleLockService.SignOutRequested += IdleLockService_SignOutRequested;

        // Admins land on the dashboard, technicians/radiologists on their work queue, reception on Patients.
        if (SessionService.Can(Permission.ViewExamOrders) && SessionService.Can(Permission.ViewAuditLog))
            ShowContent(new DashboardView(_apiClient, OpenExamQueue));
        else if (SessionService.Can(Permission.ViewExamOrders) && ExamsView.HasRoleQueue)
            ShowContent(new ExamsView(_apiClient));
        else if (SessionService.Can(Permission.ViewPatients))
            ShowPatients();
        else if (SessionService.Can(Permission.ViewExamOrders))
            ShowContent(new ExamsView(_apiClient));
    }

    private void ApiClient_SessionExpired() =>
        Dispatcher.Invoke(() => ForceSignOut((string)FindResource("Session.Expired")));

    private void IdleLockService_SignOutRequested() =>
        Dispatcher.Invoke(() => ForceSignOut(null));

    private void ShowContent(UserControl view)
    {
        ContentHost.Children.Clear();
        ContentHost.Children.Add(view);

        // Highlight the sidebar item for the screen now showing.
        Button? active = view switch
        {
            DashboardView => NavDashboard,
            ExamsView => NavExams,
            StatisticsView => NavStatistics,
            PatientsView => NavPatients,
            InvoicesView => NavInvoices,
            ExamTypesView => NavExamTypes,
            ReferringPhysiciansView => NavReferringPhysicians,
            UserManagementView => NavUsers,
            CustomRolesView => NavCustomRoles,
            AuditLogView => NavAuditLog,
            BackupView => NavBackup,
            ClinicSettingsView => NavClinicSettings,
            _ => null,
        };
        foreach (var button in new[]
                 {
                     NavDashboard, NavExams, NavStatistics, NavPatients, NavInvoices, NavExamTypes,
                     NavReferringPhysicians, NavUsers, NavCustomRoles, NavAuditLog, NavBackup, NavClinicSettings,
                 })
            button.Tag = button == active ? "Active" : null;
    }

    private void ShowPatients() => ShowContent(new PatientsView(_apiClient));

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => ShowContent(new DashboardView(_apiClient, OpenExamQueue));

    private void NavStatistics_Click(object sender, RoutedEventArgs e) => ShowContent(new StatisticsView(_apiClient));

    // Dashboard queue cards jump straight to that queue on the Exams screen.
    private void OpenExamQueue(string queue) => ShowContent(new ExamsView(_apiClient, queue));

    private void NavCheckIn_Click(object sender, RoutedEventArgs e)
    {
        var window = new CheckInWindow(_apiClient) { Owner = this };
        window.ShowDialog();

        // Land on the register so the new visit is visible straight away.
        if (window.CheckedIn)
        {
            if (SessionService.Can(Permission.ViewExamOrders)) ShowContent(new ExamsView(_apiClient));
            else ShowPatients();
        }
    }

    private void NavExams_Click(object sender, RoutedEventArgs e) => ShowContent(new ExamsView(_apiClient));

    private void NavPatients_Click(object sender, RoutedEventArgs e) => ShowPatients();

    private void NavInvoices_Click(object sender, RoutedEventArgs e) => ShowContent(new InvoicesView(_apiClient));

    private void NavExamTypes_Click(object sender, RoutedEventArgs e) => ShowContent(new ExamTypesView(_apiClient));

    private void NavReferringPhysicians_Click(object sender, RoutedEventArgs e) => ShowContent(new ReferringPhysiciansView(_apiClient));

    private void NavUsers_Click(object sender, RoutedEventArgs e) => ShowContent(new UserManagementView(_apiClient));

    private void NavCustomRoles_Click(object sender, RoutedEventArgs e) => ShowContent(new CustomRolesView(_apiClient));

    private void NavAuditLog_Click(object sender, RoutedEventArgs e) => ShowContent(new AuditLogView(_apiClient));

    private void NavClinicSettings_Click(object sender, RoutedEventArgs e) => ShowContent(new ClinicSettingsView(_apiClient));

    private void SetupBanner_Click(object sender, RoutedEventArgs e) => ShowContent(new ClinicSettingsView(_apiClient));

    private void UpdateSetupBanner() =>
        SetupBanner.Visibility = !ClinicInfoService.IsConfigured && SessionService.Can(Permission.ManageClinicSettings)
            ? Visibility.Visible : Visibility.Collapsed;

    private void NavBackup_Click(object sender, RoutedEventArgs e) => ShowContent(new BackupView(_apiClient));

    private void LanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        LanguageService.Toggle();
        var settings = ClientSettings.Load();
        settings.Language = Helpers.AppSession.Language.ToString();
        settings.Save();
    }

    private void CalendarToggle_Click(object sender, RoutedEventArgs e)
    {
        Helpers.AppSession.CalendarType = Helpers.AppSession.CalendarType == Helpers.CalendarType.Shamsi
            ? Helpers.CalendarType.Gregorian
            : Helpers.CalendarType.Shamsi;

        var settings = ClientSettings.Load();
        settings.CalendarType = Helpers.AppSession.CalendarType.ToString();
        settings.Save();
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordDialog(_apiClient) { Owner = this };
        dialog.ShowDialog();
    }

    private void SignOut_Click(object sender, RoutedEventArgs e) => ForceSignOut(null);

    private void ForceSignOut(string? message)
    {
        _apiClient.SessionExpired -= ApiClient_SessionExpired;
        IdleLockService.SignOutRequested -= IdleLockService_SignOutRequested;
        IdleLockService.Stop();
        SessionService.End();
        _apiClient.ClearSession();

        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        Close();

        if (message is not null)
            MessageBox.Show(login, message, (string)login.FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
