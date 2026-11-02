using System.Windows;
using NasimXray.Client.Views;

namespace NasimXray.Client.Services;

// The "Check for updates" button (sign-in screen and sidebar): runs a check that ignores the once-a-day wait the
// automatic start-up check observes, reports the outcome in plain words, and opens the update window if one exists.
public static class ManualUpdateCheck
{
    // show(message, isError) displays a status line; show(null, false) hides it.
    public static async Task RunAsync(Window owner, Action<string?, bool> show)
    {
        string Text(string key) => (string)owner.FindResource(key);

        show(Text("Update.Checking"), false);
        var (update, failed) = await UpdateService.CheckAsync(ignoreCooldown: true);

        if (failed)
        {
            show(Text("Update.CheckFailed"), true);
        }
        else if (update is null)
        {
            show(string.Format(Text("Update.UpToDate"), UpdateService.CurrentVersionText()), false);
        }
        else
        {
            show(null, false);
            new UpdateAvailableDialog(update) { Owner = owner }.Show();
        }
    }
}
