using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ReferringPhysicians;

namespace NasimXray.Client.Views;

public partial class ReferringPhysiciansView : UserControl
{
    private readonly ApiClient _apiClient;

    public ReferringPhysiciansView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Grid.ItemsSource = await _apiClient.GetReferringPhysiciansAsync(includeInactive: true);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ReferringPhysicianEditDialog(_apiClient) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private async void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not ReferringPhysicianDto physician) return;
        var dialog = new ReferringPhysicianEditDialog(_apiClient, physician) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }
}
