using System.Windows;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class LoginWindow : Window
{
    private ClientSettings _settings;
    private ApiClient? _apiClient;

    public LoginWindow()
    {
        InitializeComponent();
        _settings = ClientSettings.Load();
        RefreshServerAddressText();

        if (string.IsNullOrWhiteSpace(_settings.ServerBaseUrl))
        {
            Loaded += (_, _) => OpenServerSettings();
        }
    }

    private void RefreshServerAddressText()
    {
        ServerAddressText.Text = string.IsNullOrWhiteSpace(_settings.ServerBaseUrl)
            ? (string)FindResource("Login.ServerNotConfigured")
            : $"{(string)FindResource("Login.ServerPrefix")}{_settings.ServerBaseUrl}";
    }

    private void OpenServerSettings()
    {
        var dialog = new ServerSettingsWindow(_settings.ServerBaseUrl) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SavedServerUrl is not null)
        {
            _settings.ServerBaseUrl = dialog.SavedServerUrl;
            _settings.Save();
            RefreshServerAddressText();
        }
    }

    private void ServerSettings_Click(object sender, RoutedEventArgs e) => OpenServerSettings();

    private void LanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        LanguageService.Toggle();
        _settings.Language = Helpers.AppSession.Language.ToString();
        _settings.Save();
        RefreshServerAddressText();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(_settings.ServerBaseUrl))
        {
            ShowError((string)FindResource("Login.ConfigureServerFirst"));
            return;
        }

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowError((string)FindResource("Login.EnterCredentials"));
            return;
        }

        SignInButton.IsEnabled = false;
        try
        {
            _apiClient = new ApiClient(_settings.ServerBaseUrl);
            var login = await _apiClient.LoginAsync(username, password);

            _apiClient.SetAccessToken(login.AccessToken);
            SessionService.Start(login);

            if (login.MustChangePassword)
            {
                var changePassword = new ChangePasswordDialog(_apiClient, required: true) { Owner = this };
                if (changePassword.ShowDialog() != true)
                {
                    // User could not/would not complete the forced change — abandon this login attempt.
                    SessionService.End();
                    _apiClient.SetAccessToken(null);
                    return;
                }
            }

            var main = new MainWindow(_apiClient);
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (ApiException ex)
        {
            ShowError((string)FindResource($"Api.{ex.Reason}"));
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
