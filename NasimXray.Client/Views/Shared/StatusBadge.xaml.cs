using System.Windows;
using System.Windows.Controls;

namespace NasimXray.Client.Views.Shared;

// A small coloured pill for statuses. Kind picks the colour family from the palette:
// Ok (green), Warn (amber), Info (cyan), Blue, Danger (red), Neutral (grey).
public partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge),
            new PropertyMetadata(string.Empty, (d, _) => ((StatusBadge)d).Refresh()));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(StatusBadge),
            new PropertyMetadata("Neutral", (d, _) => ((StatusBadge)d).Refresh()));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public StatusBadge()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        if (Label is null) return; // property callbacks can fire before InitializeComponent finishes
        Label.Text = Text;
        var kind = string.IsNullOrEmpty(Kind) ? "Neutral" : Kind;
        // Resource references (not copies) so the badge follows the palette dictionary.
        Pill.SetResourceReference(Border.BackgroundProperty, $"Badge{kind}Bg");
        Label.SetResourceReference(TextBlock.ForegroundProperty, $"Badge{kind}Fg");
    }
}
