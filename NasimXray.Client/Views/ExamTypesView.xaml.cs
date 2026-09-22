using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamTypes;

namespace NasimXray.Client.Views;

public partial class ExamTypesView : UserControl
{
    private readonly ApiClient _apiClient;

    public ExamTypesView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Grid.ItemsSource = await _apiClient.GetExamTypesAsync(includeInactive: true);
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExamTypeEditDialog(_apiClient) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        await LoadAsync();
    }

    private async void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not ExamTypeDto examType) return;
        var dialog = new ExamTypeEditDialog(_apiClient, examType) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        await LoadAsync();
    }
}
