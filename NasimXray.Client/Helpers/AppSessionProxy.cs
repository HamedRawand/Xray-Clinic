using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;

namespace NasimXray.Client.Helpers;

// INotifyPropertyChanged wrapper around AppSession — WPF Style Setters / x:Static bindings
// cannot bind directly to static properties or static events.
public sealed class AppSessionProxy : INotifyPropertyChanged
{
    public static readonly AppSessionProxy Instance = new();

    private AppSessionProxy()
    {
        AppSession.CalendarTypeChanged += () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShamsi)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DatePickerLanguage)));
        };
        AppSession.LanguageChanged += () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDari)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        };
    }

    public bool IsShamsi => AppSession.CalendarType == CalendarType.Shamsi;

    public XmlLanguage DatePickerLanguage =>
        AppSession.CalendarType == CalendarType.Shamsi
            ? XmlLanguage.GetLanguage("fa-IR")
            : XmlLanguage.GetLanguage("en-US");

    public bool IsDari => AppSession.Language == AppLanguage.Dari;

    public FlowDirection FlowDirection => IsDari ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public event PropertyChangedEventHandler? PropertyChanged;
}
