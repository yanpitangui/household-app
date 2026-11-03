namespace HouseholdApp.Application.Modules.Expenses.Domain;

/// <summary>
/// What removing a member does to a recurring split (ADR 0015): future spending stops reaching them. Their share goes
/// to the others in proportion, whole cents by largest remainder; a split they pay for, or one left charging only its
/// payers, ends.
/// </summary>
public static class MemberRemoval
{
    public abstract record Outcome;
    public sealed record Unaffected : Outcome;
    public sealed record Ended : Outcome;
    public sealed record Redistributed(IReadOnlyList<Allocation> Allocations) : Outcome;

    public static Outcome For(IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations, Guid userId)
    {
        if (fundingSources.Any(f => f.UserId == userId)) return new Ended();
        if (allocations.All(a => a.UserId != userId)) return new Unaffected();
        var removed = allocations.Where(a => a.UserId == userId).Sum(a => a.Cents);

        var others = allocations.Where(a => a.UserId != userId).OrderBy(a => a.UserId).ToList();
        var payers = fundingSources.Select(f => f.UserId).ToHashSet();
        if (others.Count == 0 || others.All(a => payers.Contains(a.UserId))) return new Ended();

        var basis = others.Sum(a => a.Cents);
        var weights = basis == 0 ? others.Select(_ => 1L).ToList() : others.Select(a => a.Cents).ToList();
        var weightSum = weights.Sum();

        var exact = others
            .Select((a, i) => (a.UserId, a.Cents, Div: Int128.DivRem((Int128)removed * weights[i], weightSum)))
            .ToList();
        var leftover = (int)(removed - exact.Sum(e => (long)e.Div.Quotient));
        var extra = exact
            .Select((e, i) => (Index: i, e.Div.Remainder, e.UserId))
            .OrderByDescending(x => x.Remainder).ThenBy(x => x.UserId)
            .Take(leftover).Select(x => x.Index).ToHashSet();

        return new Redistributed(exact
            .Select((e, i) => new Allocation(e.UserId, e.Cents + (long)e.Div.Quotient + (extra.Contains(i) ? 1 : 0)))
            .ToList());
    }
}
