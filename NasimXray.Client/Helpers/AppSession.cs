namespace NasimXray.Client.Helpers;

// UI-preference session state (language + calendar) — distinct from Services.SessionService,
// which holds the logged-in user's identity/JWT for the app lifetime.
public static class AppSession
{
    private static CalendarType _calendarType = CalendarType.Gregorian;
    private static AppLanguage _language = AppLanguage.English;

    public static CalendarType CalendarType
    {
        get => _calendarType;
        set
        {
            if (_calendarType == value) return;
            _calendarType = value;
            CalendarTypeChanged?.Invoke();
        }
    }

    public static AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            LanguageChanged?.Invoke();
        }
    }

    public static event Action? CalendarTypeChanged;
    public static event Action? LanguageChanged;
}
