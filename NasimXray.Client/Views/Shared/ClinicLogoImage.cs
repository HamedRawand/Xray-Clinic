using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NasimXray.Client.Services;

namespace NasimXray.Client.Views.Shared;

// An Image that always shows the clinic's current logo and hides itself when there is none.
// Place it with <shared:ClinicLogoImage Height="34"/>; it follows changes made in Clinic Settings live.
public class ClinicLogoImage : Image
{
    public ClinicLogoImage()
    {
        Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += (_, _) =>
        {
            ClinicInfoService.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => ClinicInfoService.Changed -= Refresh;
    }

    private void Refresh()
    {
        Source = ClinicInfoService.Logo;
        Visibility = Source is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
