using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Users;

namespace NasimXray.Client.Views;

public partial class UserManagementView : UserControl
{
    private readonly ApiClient _apiClient;

    public UserManagementView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Grid.ItemsSource = await _apiClient.GetUsersAsync();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new UserEditDialog(_apiClient) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not UserDto user) return;
        var dialog = new UserEditDialog(_apiClient, user) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not UserDto user) return;
        var dialog = new ResetPasswordDialog(_apiClient, user.Id) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not UserDto user) return;
        try
        {
            await _apiClient.UnlockUserAsync(user.Id);
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }
}
