using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

/// <summary>
/// The payment that clears the viewer's balance with one member: the debtor pays the creditor the full
/// amount. <see cref="Cents"/> is null when there is no balance to clear.
/// </summary>
public sealed record SettleDefaults(Guid PayerId, Guid RecipientId, long? Cents)
{
    /// <param name="balances">Viewer-relative, largest first (as returned by IExpenseQueries).</param>
    public static SettleDefaults From(
        IReadOnlyList<MemberBalance> balances, Guid viewerId, Guid? withId, IReadOnlyList<Guid> memberIds)
    {
        var counterpart = withId is { } id && id != viewerId && memberIds.Contains(id) ? id : (Guid?)null;
        var live = balances.Where(b => memberIds.Contains(b.UserId)).ToList();
        var balance = counterpart is { } c
            ? live.FirstOrDefault(b => b.UserId == c && b.Cents != 0)
            : live.FirstOrDefault(b => b.Cents != 0);

        if (balance is not null)
            return balance.Cents > 0
                ? new SettleDefaults(balance.UserId, viewerId, balance.Cents)
                : new SettleDefaults(viewerId, balance.UserId, -balance.Cents);

        return new SettleDefaults(viewerId, counterpart ?? memberIds.FirstOrDefault(m => m != viewerId), null);
    }
}
