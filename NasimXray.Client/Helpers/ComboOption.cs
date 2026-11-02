namespace NasimXray.Client.Helpers;

// Item for ComboBoxes built from code: shows Label in the closed box and the list, carries Value.
public sealed record ComboOption(string Label, object Value)
{
    public override string ToString() => Label;
}
