using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

public sealed record ExpenseParseContext(
    Guid OwnerId, Guid? DefaultGroupId,
    IReadOnlyList<NameCandidate> Members, IReadOnlyList<NameCandidate> Groups, CultureInfo Culture);

public sealed record ExpenseParse(
    long? AmountCents, string? Description, NameCandidate? Group, NameCandidate? Payer,
    IReadOnlyDictionary<string, ParsedValue> Parsed, IReadOnlyList<IntakeIssue> Issues, IReadOnlyList<string> Unparsed);

// Reads one sentence into an expense: amount first, then payer, then group; whatever text is left is the
// description. Each step blanks out the text it used so later steps only see the rest.
public static class ExpenseTextParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex PaidBy = new(
        @"\b(?:paid\s+by|pago\s+por|pago\s+pel[oa])\s+(?<first>\p{L}+)(?:\s+(?<second>\p{L}+))?", Options);

    private static readonly Regex NamePaid = new(@"\b(?<first>\p{L}+)\s+(?:paid|pagou)\b", Options);

    private static readonly HashSet<string> Self = new[] { "mim", "me", "myself", "eu" }.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PaymentMethods = new[]
    {
        "card", "cash", "pix", "credit", "debit", "cartao", "cartão", "credito", "crédito", "debito", "débito",
        "dinheiro", "boleto", "check", "cheque", "transfer", "transferencia", "transferência", "nubank", "paypal",
    }.Select(TextNormalizer.Normalize).ToHashSet();

    public static ExpenseParse Parse(ExpenseInput input, ExpenseParseContext ctx)
    {
        // Composed (FormC) text keeps token offsets aligned with the characters the consumer blanks out.
        var text = new ConsumableText((input.Text ?? "").Normalize(NormalizationForm.FormC));
        var parsed = new Dictionary<string, ParsedValue>();
        var issues = new List<IntakeIssue>();
        var unparsed = new List<string>();

        var cents = ResolveAmount(input, ctx, text, parsed, issues);
        var payer = ResolvePayer(input, ctx, text, parsed, issues);
        var group = ResolveGroup(input, ctx, text, parsed, issues);
        var description = ResolveDescription(input, text, parsed, issues, unparsed);
        return new ExpenseParse(cents, description, group, payer, parsed, issues, unparsed);
    }

    private static long? ResolveAmount(
        ExpenseInput input, ExpenseParseContext ctx, ConsumableText text,
        Dictionary<string, ParsedValue> parsed, List<IntakeIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(input.Amount))
        {
            var pick = AmountParser.Pick(AmountParser.FindAll(input.Amount, ctx.Culture));
            if (pick.Hit is not { Cents: > 0 } hit)
            {
                issues.Add(new IntakeIssue(IntakeFields.Amount,
                    pick.Kind == MatchKind.Ambiguous ? IssueReason.Ambiguous : IssueReason.NotFound, pick.Candidates));
                return null;
            }
            // The same number in the sentence is the one the client was asked about, so it leaves the description.
            if (AmountParser.FindAll(text.Current, ctx.Culture).FirstOrDefault(h => h.Cents == hit.Cents) is { } echo)
                text.Consume(echo.Start, echo.Length);
            parsed[IntakeFields.Amount] = new ParsedValue(Format(hit.Cents, ctx.Culture), ValueSource.Explicit);
            return hit.Cents;
        }

        var found = AmountParser.Pick(AmountParser.FindAll(text.Current, ctx.Culture));
        if (found.Hit is { Cents: > 0 } spoken)
        {
            text.Consume(spoken.Start, spoken.Length);
            parsed[IntakeFields.Amount] = new ParsedValue(Format(spoken.Cents, ctx.Culture), ValueSource.Text);
            return spoken.Cents;
        }
        issues.Add(found.Kind == MatchKind.Ambiguous
            ? new IntakeIssue(IntakeFields.Amount, IssueReason.Ambiguous, found.Candidates)
            : new IntakeIssue(IntakeFields.Amount, IssueReason.Missing, []));
        return null;
    }

    private static NameCandidate? ResolvePayer(
        ExpenseInput input, ExpenseParseContext ctx, ConsumableText text,
        Dictionary<string, ParsedValue> parsed, List<IntakeIssue> issues)
    {
        // Callers pass members that include the owner.
        var owner = ctx.Members.First(m => m.Id == ctx.OwnerId);

        if (!string.IsNullOrWhiteSpace(input.Payer))
        {
            if (Self.Contains(input.Payer.Trim())) return Use(owner, ValueSource.Explicit);
            var match = NameMatcher.Match(input.Payer, ctx.Members);
            if (match.Kind == MatchKind.Found) return Use(match.Single, ValueSource.Explicit);
            issues.Add(Issue(IntakeFields.Payer, match, ctx.Members));
            return null;
        }

        if (PaidBy.Match(text.Current) is { Success: true } by)
        {
            var first = by.Groups["first"];
            var firstEnd = first.Index + first.Length;
            if (Self.Contains(first.Value))
            {
                text.Consume(by.Index, firstEnd - by.Index);
                return Use(owner, ValueSource.Text);
            }
            var single = NameMatcher.Match(first.Value, ctx.Members);
            if (single.Kind == MatchKind.Found)
            {
                text.Consume(by.Index, firstEnd - by.Index);
                return Use(single.Single, ValueSource.Text);
            }
            if (PaymentMethods.Contains(TextNormalizer.Normalize(first.Value)))
            {
                // "paid by card" names the payment method: the phrase leaves the description and the owner pays.
                text.Consume(by.Index, firstEnd - by.Index);
                return Use(owner, ValueSource.Default);
            }
            if (by.Groups["second"].Success
                && NameMatcher.Match($"{first.Value} {by.Groups["second"].Value}", ctx.Members) is { Kind: MatchKind.Found } both)
            {
                text.Consume(by.Index, by.Length);
                return Use(both.Single, ValueSource.Text);
            }
            issues.Add(Issue(IntakeFields.Payer, single, ctx.Members));
            return null;
        }

        if (NamePaid.Match(text.Current) is { Success: true } paid
            && NameMatcher.Match(paid.Groups["first"].Value, ctx.Members) is { Kind: MatchKind.Found } who)
        {
            text.Consume(paid.Index, paid.Length);
            return Use(who.Single, ValueSource.Text);
        }

        return Use(owner, ValueSource.Default);

        NameCandidate Use(NameCandidate member, ValueSource source)
        {
            parsed[IntakeFields.Payer] = new ParsedValue(member.Name, source);
            return member;
        }
    }

    private static NameCandidate? ResolveGroup(
        ExpenseInput input, ExpenseParseContext ctx, ConsumableText text,
        Dictionary<string, ParsedValue> parsed, List<IntakeIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(input.Group))
        {
            var match = NameMatcher.Match(input.Group, ctx.Groups);
            if (match.Kind == MatchKind.Found) return Use(match.Single, ValueSource.Explicit);
            issues.Add(Issue(IntakeFields.Group, match, ctx.Groups));
            return null;
        }

        var tokens = TextTokens.Tokenize(text.Current);
        var span = NameMatcher.FindIn(tokens, ctx.Groups);
        if (span.Match.Kind == MatchKind.Found)
        {
            var first = tokens[span.TokenStart];
            var last = tokens[span.TokenStart + span.TokenCount - 1];
            text.Consume(first.Start, last.Start + last.Length - first.Start);
            return Use(span.Match.Single, ValueSource.Text);
        }
        if (span.Match.Kind == MatchKind.Ambiguous)
        {
            issues.Add(Issue(IntakeFields.Group, span.Match, ctx.Groups));
            return null;
        }

        var fallback = ctx.Groups.FirstOrDefault(g => g.Id == ctx.DefaultGroupId)
            ?? (ctx.Groups.Count == 1 ? ctx.Groups[0] : null);
        if (fallback is not null) return Use(fallback, ValueSource.Default);
        issues.Add(new IntakeIssue(IntakeFields.Group, IssueReason.Missing, ctx.Groups.Select(g => g.Name).ToList()));
        return null;

        NameCandidate Use(NameCandidate group, ValueSource source)
        {
            parsed[IntakeFields.Group] = new ParsedValue(group.Name, source);
            return group;
        }
    }

    private static string? ResolveDescription(
        ExpenseInput input, ConsumableText text,
        Dictionary<string, ParsedValue> parsed, List<IntakeIssue> issues, List<string> unparsed)
    {
        var leftover = DescriptionCleaner.Clean(text.Current);
        if (!string.IsNullOrWhiteSpace(input.Description))
        {
            var description = input.Description.Trim();
            parsed[IntakeFields.Description] = new ParsedValue(description, ValueSource.Explicit);
            if (leftover.Length > 0) unparsed.Add(leftover);
            return description;
        }
        if (leftover.Length > 0)
        {
            parsed[IntakeFields.Description] = new ParsedValue(leftover, ValueSource.Text);
            return leftover;
        }
        issues.Add(new IntakeIssue(IntakeFields.Description, IssueReason.Missing, []));
        return null;
    }

    private static IntakeIssue Issue(string field, NameMatch match, IReadOnlyList<NameCandidate> all) =>
        match.Kind == MatchKind.Ambiguous
            ? new IntakeIssue(field, IssueReason.Ambiguous, match.Candidates.Select(c => c.Name).ToList())
            : new IntakeIssue(field, IssueReason.NotFound, all.Select(c => c.Name).ToList());

    private static string Format(long cents, CultureInfo culture) => (cents / 100m).ToString("N2", culture);
}

// A sentence whose used parts are blanked out, so positions found earlier stay valid.
internal sealed class ConsumableText(string text)
{
    private readonly char[] _chars = text.ToCharArray();

    public string Current => new(_chars);

    public void Consume(int start, int length) => Array.Fill(_chars, ' ', start, length);
}

// Connector and filler words at the edges of what is left ("eu paguei … no") are trimmed so the description keeps only what was bought.
internal static class DescriptionCleaner
{
    private static readonly HashSet<string> Filler = new[]
    {
        "em", "no", "na", "nos", "nas", "num", "numa", "in", "on", "at", "for", "para", "pra", "pro", "de", "do", "da",
        "of", "to", "the", "a", "o", "um", "uma", "eu", "i", "paguei", "gastei", "paid", "spent", "com", "with",
        "reais", "real", "dollars", "dollar", "and", "e", "my", "meu", "minha", "by", "por",
    }.Select(TextNormalizer.Normalize).ToHashSet();

    public static string Clean(string text)
    {
        var words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(',', '.', ';', ':', '!', '?', '-'))
            .Where(w => w.Length > 0)
            .ToList();
        while (words.Count > 0 && Filler.Contains(TextNormalizer.Normalize(words[0]))) words.RemoveAt(0);
        while (words.Count > 0 && Filler.Contains(TextNormalizer.Normalize(words[^1]))) words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }
}
