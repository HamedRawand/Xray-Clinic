using System.Windows;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class RestoreConfirmDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly string _fileName;

    public RestoreConfirmDialog(ApiClient apiClient, string fileName)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _fileName = fileName;
        FileText.Text = $"{(string)FindResource("RestoreConfirm.FileLabel")}{fileName}";
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (ConfirmBox.Text != "RESTORE")
        {
            ShowError((string)FindResource("RestoreConfirm.ConfirmationMismatch"));
            return;
        }

        try
        {
            await _apiClient.RestoreBackupAsync(_fileName, PasswordBox.Password);
            MessageBox.Show(this, (string)FindResource("RestoreConfirm.Success"),
                (string)FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (ApiException ex)
        {
            ShowError((string)FindResource($"Api.{ex.Reason}"));
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
