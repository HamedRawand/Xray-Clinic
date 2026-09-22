using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views;

public partial class AuditLogView : UserControl
{
    public AuditLogView(ApiClient apiClient)
    {
        InitializeComponent();
        _ = LoadAsync(apiClient);
    }

    private async Task LoadAsync(ApiClient apiClient)
    {
        try
        {
            var logs = await apiClient.GetAuditLogsAsync();
            Grid.ItemsSource = logs;
            EmptyText.Visibility = logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }
}
