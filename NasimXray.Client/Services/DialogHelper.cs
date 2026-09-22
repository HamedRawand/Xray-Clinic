using System.Windows;

namespace NasimXray.Client.Services;

public static class DialogHelper
{
    public static void ShowApiError(Window owner, ApiException ex)
    {
        var message = (string)(owner.TryFindResource($"Api.{ex.Reason}") ?? ex.Message);
        MessageBox.Show(owner, message, (string)owner.FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
