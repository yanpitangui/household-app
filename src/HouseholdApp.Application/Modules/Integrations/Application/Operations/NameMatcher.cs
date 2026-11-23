using HouseholdApp.Application.Modules.Integrations.Application.Ports;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

public enum MatchKind { Found, Ambiguous, None }

public sealed record NameMatch(MatchKind Kind, IReadOnlyList<NameCandidate> Candidates)
{
    public static readonly NameMatch None = new(MatchKind.None, []);

    public NameCandidate Single => Candidates[0];

    public static NameMatch Of(IReadOnlyList<NameCandidate> candidates) => candidates.Count switch
    {
        0 => None,
        1 => new(MatchKind.Found, candidates),
        _ => new(MatchKind.Ambiguous, candidates),
    };
}

public sealed record SpanMatch(NameMatch Match, int TokenStart, int TokenCount);

public static class NameMatcher
{
    // Connector words say nothing about which name was meant, so shared-word matching ignores them.
    private static readonly HashSet<string> Connectors = new[]
    {
        "de", "do", "da", "dos", "das", "e", "a", "o", "em", "no", "na", "of", "the", "and", "in", "on", "my",
    }.Select(TextNormalizer.Normalize).ToHashSet();

    // Tiers in order: same words, one name contained in the other, most shared words. The first tier with a hit
    // decides; more than one hit there is ambiguous.
    public static NameMatch Match(string phrase, IReadOnlyList<NameCandidate> candidates, bool allowOverlap = true)
    {
        var words = TextNormalizer.Words(phrase);
        if (words.Count == 0 || words.All(Connectors.Contains)) return NameMatch.None;
        var named = candidates
            .Select(c => (Candidate: c, Words: TextNormalizer.Words(c.Name)))
            .Where(x => x.Words.Count > 0)
            .ToList();

        var exact = named.Where(x => x.Words.SequenceEqual(words)).Select(x => x.Candidate).ToList();
        if (exact.Count > 0) return NameMatch.Of(exact);

        var inPhrase = named.Where(x => IndexOf(words, x.Words) >= 0).ToList();
        var holdsPhrase = named.Where(x => IndexOf(x.Words, words) >= 0).Select(x => x.Candidate).ToList();
        if (inPhrase.Count > 0)
        {
            // Longest contained name wins, as in FindIn; it beats names that merely hold the phrase only when alone.
            var longest = inPhrase.Max(x => x.Words.Count);
            var top = inPhrase.Where(x => x.Words.Count == longest).Select(x => x.Candidate).ToList();
            if (holdsPhrase.Count == 0 || top.Count == 1) return NameMatch.Of(top);
            return NameMatch.Of(top.Concat(holdsPhrase).Distinct().ToList());
        }
        if (holdsPhrase.Count > 0) return NameMatch.Of(holdsPhrase);
        if (!allowOverlap) return NameMatch.None;

        var meaningful = words.Where(w => !Connectors.Contains(w)).ToHashSet();
        var scored = named
            .Select(x => (x.Candidate, Shared: x.Words.Distinct().Count(meaningful.Contains)))
            .Where(x => x.Shared > 0)
            .ToList();
        if (scored.Count == 0) return NameMatch.None;
        var best = scored.Max(x => x.Shared);
        return NameMatch.Of(scored.Where(x => x.Shared == best).Select(x => x.Candidate).ToList());
    }

    // Finds a whole candidate name inside a sentence; the longest name found wins.
    public static SpanMatch FindIn(IReadOnlyList<TextToken> tokens, IReadOnlyList<NameCandidate> candidates)
    {
        var norms = tokens.Select(t => t.Norm).ToList();
        var hits = new List<(NameCandidate Candidate, int Start, int Count)>();
        foreach (var candidate in candidates)
        {
            var words = TextNormalizer.Words(candidate.Name);
            if (words.Count == 0) continue;
            var at = IndexOf(norms, words);
            if (at >= 0) hits.Add((candidate, at, words.Count));
        }
        if (hits.Count == 0) return new(NameMatch.None, -1, 0);

        var longest = hits.Max(h => h.Count);
        var top = hits.Where(h => h.Count == longest).ToList();
        return top.Count == 1
            ? new(NameMatch.Of([top[0].Candidate]), top[0].Start, top[0].Count)
            : new(NameMatch.Of(top.Select(h => h.Candidate).ToList()), -1, 0);
    }

    private static int IndexOf(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        for (var i = 0; i + needle.Count <= haystack.Count; i++)
        {
            var all = true;
            for (var j = 0; j < needle.Count && all; j++) all = haystack[i + j] == needle[j];
            if (all) return i;
        }
        return -1;
    }
}
