using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Client.Views;
using NasimXray.Shared.Enums;

namespace NasimXray.Client;

public partial class MainWindow : Window
{
    private readonly ApiClient _apiClient;

    public MainWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        var user = SessionService.Current;
        UserText.Text = user is null ? string.Empty : $"{user.FullName} ({user.RoleDisplayName})";

        NavUsers.Visibility = SessionService.Can(Permission.ManageUsers) ? Visibility.Visible : Visibility.Collapsed;
        NavCustomRoles.Visibility = SessionService.Can(Permission.ManageRoles) ? Visibility.Visible : Visibility.Collapsed;
        NavAuditLog.Visibility = SessionService.Can(Permission.ViewAuditLog) ? Visibility.Visible : Visibility.Collapsed;
        NavBackup.Visibility = SessionService.Can(Permission.ManageBackup) ? Visibility.Visible : Visibility.Collapsed;

        ShowPatients();
    }

    private void ShowContent(UserControl view)
    {
        ContentHost.Children.Clear();
        ContentHost.Children.Add(view);
    }

    private void ShowPatients() => ShowContent(new PatientsView(_apiClient));

    private void NavPatients_Click(object sender, RoutedEventArgs e) => ShowPatients();

    private void NavInvoices_Click(object sender, RoutedEventArgs e) => ShowContent(new InvoicesView(_apiClient));

    private void NavExamTypes_Click(object sender, RoutedEventArgs e) => ShowContent(new ExamTypesView(_apiClient));

    private void NavReferringPhysicians_Click(object sender, RoutedEventArgs e) => ShowContent(new ReferringPhysiciansView(_apiClient));

    private void NavUsers_Click(object sender, RoutedEventArgs e) => ShowContent(new UserManagementView(_apiClient));

    private void NavCustomRoles_Click(object sender, RoutedEventArgs e) => ShowContent(new CustomRolesView(_apiClient));

    private void NavAuditLog_Click(object sender, RoutedEventArgs e) => ShowContent(new AuditLogView(_apiClient));

    private void NavBackup_Click(object sender, RoutedEventArgs e) => ShowContent(new BackupView(_apiClient));

    private void LanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        LanguageService.Toggle();
        var settings = ClientSettings.Load();
        settings.Language = Helpers.AppSession.Language.ToString();
        settings.Save();
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordDialog(_apiClient) { Owner = this };
        dialog.ShowDialog();
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        SessionService.End();
        _apiClient.SetAccessToken(null);

        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        Close();
    }
}
