using System.Globalization;

namespace NasimXray.Shared;

// The clinic charges in Afghanis. Every amount shown anywhere (screens, receipts, spreadsheets, server
// messages) goes through here, so the currency and number format are defined in exactly one place.
public static class Currency
{
    public const string Code = "AFN";

    // 1500 -> "1,500.00 AFN". Invariant culture so the format doesn't change with the PC's regional settings.
    public static string Format(decimal amount) =>
        $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {Code}";

    public static string Format(decimal? amount) => amount.HasValue ? Format(amount.Value) : string.Empty;
}
