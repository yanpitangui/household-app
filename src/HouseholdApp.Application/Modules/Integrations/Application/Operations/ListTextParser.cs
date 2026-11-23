using System.Text;
using System.Text.RegularExpressions;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

public sealed record ItemPiece(string Text, string? JoinerBefore);

public sealed record ListParseContext(Guid? DefaultListId, IReadOnlyList<NameCandidate> Lists);

public sealed record ListTextParse(
    NameCandidate? List, IReadOnlyList<ItemPiece> Pieces,
    IReadOnlyDictionary<string, ParsedValue> Parsed, IReadOnlyList<IntakeIssue> Issues);

public static class ListTextParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex Command = new(
        @"^\s*(?:add|adiciona(?:r)?|adicione|coloca(?:r)?|coloque|put|buy|compra(?:r)?|inclui(?:r)?)\b\s*", Options);

    private static readonly Regex Connector = new(@"(?:^|\s)(?:to|on|in|into|para|pra|na|no|em)\s", Options);

    private static readonly Regex Tail = new(
        @"^(?:(?:the|my|a|o|minha|meu)\s+)?(?<kw>(?:list|lista)\b\s*(?:(?:de|do|da|of)\s+)?)?(?<name>.+?)[\s.!?]*$", Options);

    private static readonly Regex Separator = new(@"(\s*(?:;|(?<!\d),|,(?!\d))\s*|\s+(?:and|e|&)\s+)", Options);

    public static ListTextParse Parse(ListInput input, ListParseContext ctx)
    {
        var parsed = new Dictionary<string, ParsedValue>();
        var issues = new List<IntakeIssue>();
        // Composed (FormC) text keeps offsets consistent with the tokenizer used for name matching.
        var text = input.Text is null ? "" : Command.Replace(input.Text.Normalize(NormalizationForm.FormC).Trim(), "", 1);
        NameCandidate? list = null;
        var settled = false;

        if (!string.IsNullOrWhiteSpace(input.List))
        {
            settled = true;
            var match = NameMatcher.Match(input.List, ctx.Lists);
            if (match.Kind == MatchKind.Found) list = Use(match.Single, ValueSource.Explicit);
            else issues.Add(Issue(match, ctx.Lists));
        }
        else
        {
            // The last connector is tried first: "pão na chapa to groceries" names the list after "to".
            foreach (var connector in Connector.Matches(text).Reverse())
            {
                var tail = Tail.Match(text[(connector.Index + connector.Length)..]);
                if (!tail.Success || tail.Groups["name"].Value.Contains(',')) continue;
                var match = NameMatcher.Match(tail.Groups["name"].Value, ctx.Lists, allowOverlap: false);
                // Without the word "list", a connector naming no list is part of an item ("pão na chapa").
                if (match.Kind == MatchKind.None && !tail.Groups["kw"].Success) continue;

                var remainder = "";
                if (match.Kind == MatchKind.Found)
                {
                    var tailName = tail.Groups["name"].Value;
                    var nameWords = TextNormalizer.Words(match.Single.Name);
                    var spoken = TextNormalizer.Words(tailName);
                    if (StartsWith(spoken, nameWords))
                    {
                        // Text after the list name is more items only when a joiner leads it ("mercado e pão").
                        if (AfterName(tailName, nameWords.Count) is not { } rest) continue;
                        remainder = rest;
                    }
                    // A list name found in the middle of the tail belongs to a longer phrase, so the next connector is tried.
                    else if (!Contains(nameWords, spoken)) continue;
                }

                text = text[..connector.Index] + (remainder.Length > 0 ? ", " + remainder : "");
                settled = true;
                if (match.Kind == MatchKind.Found) list = Use(match.Single, ValueSource.Text);
                else issues.Add(Issue(match, ctx.Lists));
                break;
            }
        }

        if (!settled)
        {
            var fallback = ctx.Lists.FirstOrDefault(l => l.Id == ctx.DefaultListId)
                ?? (ctx.Lists.Count == 1 ? ctx.Lists[0] : null);
            if (fallback is not null) list = Use(fallback, ValueSource.Default);
            else issues.Add(new IntakeIssue(IntakeFields.List, IssueReason.Missing, ctx.Lists.Select(l => l.Name).ToList()));
        }

        var pieces = input.Text is null ? [] : Split(text);
        if (input.Items is null && pieces.Count == 0)
            issues.Add(new IntakeIssue(IntakeFields.Items, IssueReason.Missing, []));
        return new ListTextParse(list, pieces, parsed, issues);

        NameCandidate Use(NameCandidate candidate, ValueSource source)
        {
            parsed[IntakeFields.List] = new ParsedValue(candidate.Name, source);
            return candidate;
        }
    }

    private static readonly Regex LeadingJoiner = new(@"^\s*(?:and\b|e\b|&|,|;)\s*", Options);

    private static readonly Regex TrailingPoliteness = new(@"\b(?:list|lista|please|por\s+favor)[\s.!?]*$", Options);

    // What follows the list name ("mercado e pão" → "pão"): empty after dropping "list"/"please", the items after a
    // leading joiner, or null when other words follow, so the phrase stays part of an item.
    private static string? AfterName(string tailName, int nameWordCount)
    {
        var tokens = TextTokens.Tokenize(tailName);
        if (tokens.Count < nameWordCount) return "";
        var end = tokens[nameWordCount - 1].Start + tokens[nameWordCount - 1].Length;
        var rest = tailName.Normalize(NormalizationForm.FormC)[end..].Trim();
        for (var next = TrailingPoliteness.Replace(rest, "").Trim(); next != rest; next = TrailingPoliteness.Replace(rest, "").Trim())
            rest = next;
        if (rest.Length == 0) return "";
        return LeadingJoiner.IsMatch(rest) ? LeadingJoiner.Replace(rest, "", 1).Trim() : null;
    }

    private static bool StartsWith(IReadOnlyList<string> words, IReadOnlyList<string> prefix) =>
        prefix.Count <= words.Count && words.Take(prefix.Count).SequenceEqual(prefix);

    private static bool Contains(IReadOnlyList<string> words, IReadOnlyList<string> part) =>
        part.Count <= words.Count && Enumerable.Range(0, words.Count - part.Count + 1).Any(i => words.Skip(i).Take(part.Count).SequenceEqual(part));

    private static IntakeIssue Issue(NameMatch match, IReadOnlyList<NameCandidate> lists) =>
        match.Kind == MatchKind.Ambiguous
            ? new IntakeIssue(IntakeFields.List, IssueReason.Ambiguous, match.Candidates.Select(c => c.Name).ToList())
            : new IntakeIssue(IntakeFields.List, IssueReason.NotFound, lists.Select(l => l.Name).ToList());

    // Regex.Split keeps the captured separators: [piece, separator, piece, …]. A word joiner ("and"/"e") is kept so
    // the builder can rejoin names like "arroz e feijão"; a comma always separates.
    private static List<ItemPiece> Split(string text)
    {
        var parts = Separator.Split(text);
        var pieces = new List<ItemPiece>();
        string? joiner = null;
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                var separator = parts[i].Trim();
                joiner = separator is "," or ";" ? null : separator;
                continue;
            }
            var piece = parts[i].Trim().TrimEnd('.', '!', '?');
            if (piece.Length > 0) pieces.Add(new ItemPiece(piece, pieces.Count == 0 ? null : joiner));
            joiner = null;
        }
        return pieces;
    }
}
