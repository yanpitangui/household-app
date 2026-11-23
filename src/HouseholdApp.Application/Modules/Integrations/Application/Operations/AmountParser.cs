using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

public sealed record AmountHit(long Cents, string Raw, int Start, int Length, bool HasCurrency, bool Malformed = false);

public sealed record AmountPick(MatchKind Kind, AmountHit? Hit, IReadOnlyList<string> Candidates);

public static class AmountParser
{
    private const long MaxCents = 100_000_000;

    private static readonly Regex Token = new(
        @"(?<![\p{L}\p{N}])(?:(?<pre>(?:US|R)?\$)\s*)?(?<num>\d+(?:[.,]\d+)*)(?!\p{N})(?<suf>\s*(?:reais|real|d[oó]lar(?:es)?|dollars?|bucks)\b)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly ConcurrentDictionary<(char Decimal, char Group), Regex> Shapes = new();

    // The whole numeric token is read first and then checked against the culture's shape, so pt-BR reads "1.234,56"
    // and en reads "1,234.56", while "12.50" in pt-BR stays one malformed token instead of two smaller numbers.
    public static IReadOnlyList<AmountHit> FindAll(string text, CultureInfo culture)
    {
        var format = culture.NumberFormat;
        var decimalSeparator = format.NumberDecimalSeparator[0];
        var groupSeparator = format.NumberGroupSeparator[0];
        var shape = Shapes.GetOrAdd((decimalSeparator, groupSeparator), key => BuildShape(key.Decimal, key.Group));

        return Token.Matches(text).Select(m =>
        {
            var hasCurrency = m.Groups["pre"].Success || m.Groups["suf"].Success;
            var cents = shape.IsMatch(m.Groups["num"].Value)
                ? ToCents(m.Groups["num"].Value, decimalSeparator, groupSeparator)
                : null;
            return new AmountHit(cents ?? 0, m.Value.Trim(), m.Index, m.Length, hasCurrency, cents is null);
        }).ToList();
    }

    // One number marked as money (R$, $, reais…) wins over bare numbers like "2 pizzas"; otherwise exactly one
    // number is needed. A malformed number or anything else is reported as ambiguous so the client asks.
    public static AmountPick Pick(IReadOnlyList<AmountHit> hits)
    {
        if (hits.Count == 0) return new(MatchKind.None, null, []);
        if (hits.Any(h => h.Malformed)) return new(MatchKind.Ambiguous, null, hits.Select(h => h.Raw).ToList());
        var marked = hits.Where(h => h.HasCurrency).ToList();
        var pool = marked.Count > 0 ? marked : hits.ToList();
        return pool.Count == 1
            ? new(MatchKind.Found, pool[0], [])
            : new(MatchKind.Ambiguous, null, pool.Select(h => h.Raw).ToList());
    }

    private static long? ToCents(string number, char decimalSeparator, char groupSeparator)
    {
        var parts = number.Replace(groupSeparator.ToString(), "").Split(decimalSeparator);
        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var integer)
            || integer > MaxCents / 100) return null;
        var fraction = parts.Length > 1 ? int.Parse(parts[1].PadRight(2, '0'), CultureInfo.InvariantCulture) : 0;
        var cents = integer * 100 + fraction;
        return cents <= MaxCents ? cents : null;
    }

    private static Regex BuildShape(char decimalSeparator, char groupSeparator)
    {
        var d = Regex.Escape(decimalSeparator.ToString());
        var g = Regex.Escape(groupSeparator.ToString());
        return new Regex($@"^(?:\d{{1,3}}(?:{g}\d{{3}})+|\d{{1,9}})(?:{d}\d{{1,2}})?$", RegexOptions.CultureInvariant);
    }
}
