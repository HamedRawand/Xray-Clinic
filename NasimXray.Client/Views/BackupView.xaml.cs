using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Backup;

namespace NasimXray.Client.Views;

public partial class BackupView : UserControl
{
    private readonly ApiClient _apiClient;

    public BackupView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var backups = await _apiClient.GetBackupsAsync();
            Grid.ItemsSource = backups;
            EmptyText.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            LastBackupText.Text = backups.Count == 0
                ? $"{(string)FindResource("Backup.LastBackup")}{(string)FindResource("Backup.Never")}"
                : $"{(string)FindResource("Backup.LastBackup")}{Helpers.CalendarFormat.Format(backups[0].CreatedAtUtc, "datetime")}";
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        CreateButton.IsEnabled = false;
        try
        {
            await _apiClient.CreateBackupAsync();
            await LoadAsync();
            MessageBox.Show(Window.GetWindow(this), (string)FindResource("Backup.Created.Success"),
                (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BackupFileDto backup) return;

        var dialog = new RestoreConfirmDialog(_apiClient, backup.FileName) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }
}
