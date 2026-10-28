using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public static class LedgerMath
{
    public static Dictionary<Guid, long> NetPerUser(
        IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations)
    {
        var net = new Dictionary<Guid, long>();
        foreach (var f in fundingSources) net[f.UserId] = net.GetValueOrDefault(f.UserId) + f.Cents;
        foreach (var a in allocations) net[a.UserId] = net.GetValueOrDefault(a.UserId) - a.Cents;
        return net;
    }

    /// <summary>
    /// Who owes whom for one expense: each debtor's debt is spread over the creditors in proportion to what each
    /// is still owed, whole cents by largest remainder. Every debtor's shares add up to their debt and every
    /// creditor's to their credit, and the same nets always give the same shares, so a void reverses exactly.
    /// </summary>
    public static IEnumerable<(Guid Debtor, Guid Creditor, long Cents)> PairShares(Dictionary<Guid, long> net)
    {
        var remaining = net.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);

        foreach (var (debtor, debtorNet) in net.Where(kv => kv.Value < 0).OrderBy(kv => kv.Key))
        {
            // Balanced expenses pay creditors out exactly with the last debtor; this stop keeps unbalanced ones safe.
            var open = remaining.Values.Sum();
            if (open == 0) yield break;

            var debt = Math.Min(-debtorNet, open);
            var shares = remaining
                .Select(c => (Creditor: c.Key, Exact: Int128.DivRem((Int128)debt * c.Value, open)))
                .Select(s => (s.Creditor, Cents: (long)s.Exact.Quotient, Remainder: (long)s.Exact.Remainder))
                .ToList();

            // The floors leave fewer leftover cents than creditors; they go to the largest remainders.
            var leftover = debt - shares.Sum(s => s.Cents);
            var rounded = shares
                .OrderByDescending(s => s.Remainder).ThenBy(s => s.Creditor)
                .Select((s, rank) => (s.Creditor, Cents: s.Cents + (rank < leftover ? 1 : 0)));

            foreach (var (creditor, cents) in rounded.Where(s => s.Cents > 0))
            {
                remaining[creditor] -= cents;
                yield return (debtor, creditor, cents);
            }
        }
    }
}
