using System.Windows;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class ChangePasswordDialog : Window
{
    private readonly ApiClient _apiClient;

    public ChangePasswordDialog(ApiClient apiClient, bool required = false)
    {
        InitializeComponent();
        _apiClient = apiClient;

        if (required)
        {
            RequiredText.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            // Forced change on first login — closing via Alt+F4/X should not silently bypass it.
            Closing += (_, e) => { if (DialogResult != true) e.Cancel = true; };
        }
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (NewBox.Password.Length < 8)
        {
            ShowError((string)FindResource("UserEdit.PasswordTooShort"));
            return;
        }
        if (NewBox.Password != ConfirmBox.Password)
        {
            ShowError((string)FindResource("ChangePassword.Mismatch"));
            return;
        }

        try
        {
            await _apiClient.ChangePasswordAsync(CurrentBox.Password, NewBox.Password);
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
