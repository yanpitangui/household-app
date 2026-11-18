using System.Globalization;
using System.Resources;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

// Push notification wording in one recipient's language. Strings live in ExpenseNotificationText.resx
// (en) and ExpenseNotificationText.pt-BR.resx; amounts are formatted with the same culture.
internal sealed class ExpenseNotificationText
{
    private static readonly ResourceManager Resources = new(typeof(ExpenseNotificationText));
    private static readonly CultureInfo Default = CultureInfo.GetCultureInfo("en");
    private static readonly HashSet<string> Supported = ["en", "pt-BR"];

    private readonly CultureInfo _culture;

    private ExpenseNotificationText(CultureInfo culture) => _culture = culture;

    // Users who haven't been seen since locales were stored read English, the app's default.
    public static ExpenseNotificationText For(string? locale) =>
        new(locale is not null && Supported.Contains(locale) ? CultureInfo.GetCultureInfo(locale) : Default);

    public string Name(string? displayName) => displayName ?? Format("Someone");

    public string Money(long cents) => Format("Money", (cents / 100m).ToString("N2", _culture));

    public string Format(string key, params object[] args)
    {
        var template = Resources.GetString(key, _culture)
            ?? throw new InvalidOperationException($"Missing notification string '{key}'.");
        return args.Length == 0 ? template : string.Format(_culture, template, args);
    }
}
