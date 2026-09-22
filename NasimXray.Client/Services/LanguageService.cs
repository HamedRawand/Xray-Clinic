using System.Windows;
using NasimXray.Client.Helpers;

namespace NasimXray.Client.Services;

// Swaps the merged Strings.*.xaml ResourceDictionary at runtime — every {DynamicResource}
// binding in the app picks up the change live, no window reload needed.
public static class LanguageService
{
    private const string EnglishUri = "Resources/Strings.en.xaml";
    private const string DariUri = "Resources/Strings.dari.xaml";

    public static void Apply(AppLanguage language)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        var stringsDict = dictionaries.FirstOrDefault(d =>
            d.Source is not null &&
            (d.Source.OriginalString.EndsWith("Strings.en.xaml") ||
             d.Source.OriginalString.EndsWith("Strings.dari.xaml")));

        var uri = language == AppLanguage.Dari ? DariUri : EnglishUri;
        var newDict = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };

        if (stringsDict is not null)
        {
            var index = dictionaries.IndexOf(stringsDict);
            dictionaries[index] = newDict;
        }
        else
        {
            dictionaries.Add(newDict);
        }

        AppSession.Language = language;
    }

    public static void Toggle() =>
        Apply(AppSession.Language == AppLanguage.Dari ? AppLanguage.English : AppLanguage.Dari);
}
