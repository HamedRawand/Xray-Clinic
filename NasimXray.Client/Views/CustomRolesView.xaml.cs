using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Roles;

namespace NasimXray.Client.Views;

public partial class CustomRolesView : UserControl
{
    private readonly ApiClient _apiClient;

    public CustomRolesView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Grid.ItemsSource = await _apiClient.GetCustomRolesAsync();
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomRoleEditDialog(_apiClient) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private async void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not CustomRoleDto role) return;
        var dialog = new CustomRoleEditDialog(_apiClient, role) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }
}
