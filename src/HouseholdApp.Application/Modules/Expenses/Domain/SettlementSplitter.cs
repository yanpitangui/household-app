namespace HouseholdApp.Application.Modules.Expenses.Domain;

/// <summary>What the payer owes the recipient in one group; negative when the recipient owes the payer.</summary>
public sealed record GroupDebt(Guid GroupId, long Cents);

/// <summary>One settlement to record. Offset legs run recipient → payer and move no cash.</summary>
public sealed record SettlementLeg(Guid GroupId, long Cents, bool IsOffset);

public enum SettlementSplitError { NothingOwed, WrongDirection, AmountNotPositive, ExceedsOwed }

public sealed record SettlementSplit(
    IReadOnlyList<SettlementLeg> Legs, long OwedCents, SettlementSplitError? Error);

public sealed class SettlementSplitException(SettlementSplitError error)
    : InvalidOperationException($"The payment cannot be split across groups: {error}.")
{
    public SettlementSplitError Error { get; } = error;
}

public static class SettlementSplitter
{
    /// <summary>
    /// Splits a household payment into per-group legs. Groups where the recipient owes the payer are closed by
    /// offset legs; the cash plus those offsets then pays the payer's debts, largest first (ties by group id).
    /// Cash legs come first, so <c>Legs[0]</c> is the lead leg. The pool always fits: total debt = owed + offsets.
    /// </summary>
    public static SettlementSplit Split(IReadOnlyList<GroupDebt> debts, long cents)
    {
        var owed = debts.Sum(d => d.Cents);
        if (owed == 0) return Fail(SettlementSplitError.NothingOwed, owed);
        if (owed < 0) return Fail(SettlementSplitError.WrongDirection, owed);
        if (cents <= 0) return Fail(SettlementSplitError.AmountNotPositive, owed);
        if (cents > owed) return Fail(SettlementSplitError.ExceedsOwed, owed);

        var offsets = debts
            .Where(d => d.Cents < 0)
            .OrderBy(d => d.Cents).ThenBy(d => d.GroupId)
            .Select(d => new SettlementLeg(d.GroupId, -d.Cents, IsOffset: true))
            .ToList();

        var pool = cents + offsets.Sum(o => o.Cents);
        var cash = new List<SettlementLeg>();
        foreach (var debt in debts.Where(d => d.Cents > 0).OrderByDescending(d => d.Cents).ThenBy(d => d.GroupId))
        {
            if (pool == 0) break;
            var paid = Math.Min(debt.Cents, pool);
            cash.Add(new SettlementLeg(debt.GroupId, paid, IsOffset: false));
            pool -= paid;
        }

        return new SettlementSplit([.. cash, .. offsets], owed, null);
    }

    private static SettlementSplit Fail(SettlementSplitError error, long owed) => new([], owed, error);
}
