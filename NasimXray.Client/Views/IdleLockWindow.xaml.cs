using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class IdleLockWindow : Window
{
    private readonly ApiClient _apiClient;
    private bool _resolved;

    public event Action? Unlocked;
    public event Action? SignOutRequested;

    public IdleLockWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        UserText.Text = SessionService.Current?.FullName;

        // Alt+F4 / task-switch-close must not bypass the lock — only Unlock or Sign Out (which
        // set _resolved first) are allowed to actually close this window.
        Closing += (_, e) => { if (!_resolved) e.Cancel = true; };

        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Unlock_Click(sender, e);
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        UnlockButton.IsEnabled = false;

        try
        {
            var ok = await _apiClient.VerifyPasswordAsync(PasswordBox.Password);
            if (!ok)
            {
                ErrorText.Text = (string)FindResource("IdleLock.IncorrectPassword");
                ErrorText.Visibility = Visibility.Visible;
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            _resolved = true;
            Unlocked?.Invoke();
            Close();
        }
        catch (ApiException ex)
        {
            ErrorText.Text = (string)FindResource($"Api.{ex.Reason}");
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _resolved = true;
        SignOutRequested?.Invoke();
        Close();
    }
}
