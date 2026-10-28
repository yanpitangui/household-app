using System.Globalization;
using System.Text;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

internal readonly record struct KeysetPoint(DateTimeOffset Date, Guid Id);

internal readonly record struct TimelineCursor(KeysetPoint? Expense, KeysetPoint? Settlement, bool InRecent = false)
{
    public static KeysetPoint PointOf(ExpenseReadModel e) => new(e.Date, e.Id);

    public static KeysetPoint PointOf(SettlementReadModel s) => new(s.Date, s.Id);

    // Each source's position is optional; an absent part means that source starts at the beginning of Older.
    // Base64url keeps the cursor safe in a query string.
    // InRecent marks a Recent that was capped: its Older pages still run through the entries after the settled-up marker.
    public static string? Encode(KeysetPoint? expense, KeysetPoint? settlement, bool inRecent = false)
    {
        var parts = new List<string>();
        if (inRecent) parts.Add("r=1");
        if (expense is { } e) parts.Add($"e={Format(e)}");
        if (settlement is { } s) parts.Add($"s={Format(s)}");
        return parts.Count == 0 ? null : Pack(string.Join(';', parts));
    }

    // Starts Older at the entries before the settled-up marker; decodes to the default cursor.
    public static string StartOfOlder { get; } = Pack("o=1");

    private static string Pack(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static TimelineCursor Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return default;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            KeysetPoint? expense = null, settlement = null;
            var inRecent = false;
            foreach (var part in text.Split(';'))
            {
                if (part.StartsWith("e=", StringComparison.Ordinal)) expense = Parse(part[2..]);
                else if (part.StartsWith("s=", StringComparison.Ordinal)) settlement = Parse(part[2..]);
                else if (part == "r=1") inRecent = true;
            }

            return new TimelineCursor(expense, settlement, inRecent);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException)
        {
            return default;
        }
    }

    private static string Format(KeysetPoint p) =>
        $"{p.Date.ToString("O", CultureInfo.InvariantCulture)}|{p.Id:N}";

    private static KeysetPoint Parse(string text)
    {
        var split = text.Split('|');
        return new KeysetPoint(
            DateTimeOffset.ParseExact(split[0], "O", CultureInfo.InvariantCulture),
            Guid.ParseExact(split[1], "N"));
    }
}
