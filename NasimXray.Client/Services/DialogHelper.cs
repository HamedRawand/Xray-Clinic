using System.Windows;

namespace NasimXray.Client.Services;

public static class DialogHelper
{
    public static void ShowApiError(Window owner, ApiException ex)
    {
        var message = (string)(owner.TryFindResource($"Api.{ex.Reason}") ?? ex.Message);
        // The server's own explanation (e.g. "already invoiced") is more useful than the generic text.
        if (ex.Reason == ApiErrorReason.BadRequest && !string.IsNullOrWhiteSpace(ex.Detail))
            message += "\n\n" + ex.Detail;
        MessageBox.Show(owner, message, (string)owner.FindResource("App.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
