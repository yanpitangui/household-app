using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

public static class TextNormalizer
{
    public static string Normalize(string text) => string.Join(' ', Words(text));

    public static IReadOnlyList<string> Words(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Stem).ToList();
    }

    // Singular and plural compare equal ("despesas" = "despesa", "expenses" = "expense", "variaveis" = "variavel",
    // "limoes" = "limao"); short words keep their s.
    private static string Stem(string word)
    {
        if (word.Length > 3 && (word.EndsWith("oes", StringComparison.Ordinal) || word.EndsWith("aes", StringComparison.Ordinal)))
            return word[..^3] + "ao";
        if (word.Length > 4)
        {
            if (word.EndsWith("eis", StringComparison.Ordinal)) return word[..^3] + "el";
            if (word.EndsWith("ais", StringComparison.Ordinal)) return word[..^3] + "al";
            if (word.EndsWith("ois", StringComparison.Ordinal)) return word[..^3] + "ol";
        }
        return word.Length > 3 && word[^1] == 's' ? word[..^1] : word;
    }
}

public sealed record TextToken(string Norm, int Start, int Length);

public static class TextTokens
{
    private static readonly Regex Word = new(@"[\p{L}\p{M}\p{Nd}]+", RegexOptions.CultureInvariant);

    // Offsets refer to the FormC-composed text (callers NFC-normalize their input first).
    public static IReadOnlyList<TextToken> Tokenize(string text) =>
        Word.Matches(text.Normalize(NormalizationForm.FormC))
            .Select(m => new TextToken(TextNormalizer.Normalize(m.Value), m.Index, m.Length))
            .Where(t => t.Norm.Length > 0)
            .ToList();
}
