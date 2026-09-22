using System.Windows;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class ResetPasswordDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _userId;

    public ResetPasswordDialog(ApiClient apiClient, int userId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _userId = userId;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (NewPasswordBox.Password.Length < 8)
        {
            ShowError((string)FindResource("UserEdit.PasswordTooShort"));
            return;
        }

        try
        {
            await _apiClient.ResetUserPasswordAsync(_userId, NewPasswordBox.Password);
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
