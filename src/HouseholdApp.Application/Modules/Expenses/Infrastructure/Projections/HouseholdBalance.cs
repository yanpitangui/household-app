using HouseholdApp.Application.Modules.Expenses.Domain;
using Marten;
using Marten.Events.Projections;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public class PairEntry
{
    public Guid GroupId { get; set; }
    public Guid UserId1 { get; set; }
    public Guid UserId2 { get; set; }
    public long Cents { get; set; } // positive = UserId1 is owed by UserId2
}

public class HouseholdLedger
{
    public Guid Id { get; set; } // HouseholdId
    public List<PairEntry> Pairs { get; set; } = [];

    /// <summary>When each group last had every pair at zero; timelines show only entries recorded after it.</summary>
    public Dictionary<Guid, DateTimeOffset> GroupSettledAt { get; set; } = [];

    /// <summary>When every member pair last netted to zero across all groups.</summary>
    public DateTimeOffset? HouseholdSettledAt { get; set; }

    /// <summary>The marker before <see cref="HouseholdSettledAt"/>, restored when a settle's later legs show the household was only momentarily at zero.</summary>
    public DateTimeOffset? HouseholdSettledBefore { get; set; }

    /// <summary>
    /// What <paramref name="debtor"/> owes <paramref name="creditor"/> in each group (negative when the creditor
    /// owes), skipping settled pairs and, when given, groups outside <paramref name="activeGroups"/>.
    /// </summary>
    public IReadOnlyList<GroupDebt> DebtsBetween(Guid debtor, Guid creditor, ISet<Guid>? activeGroups = null) =>
        Pairs
            .Where(p => p.Cents != 0
                && ((p.UserId1 == creditor && p.UserId2 == debtor) || (p.UserId1 == debtor && p.UserId2 == creditor))
                && (activeGroups is null || activeGroups.Contains(p.GroupId)))
            .Select(p => new GroupDebt(p.GroupId, p.UserId1 == creditor ? p.Cents : -p.Cents))
            .ToList();
}

public partial class HouseholdLedgerProjection : MultiStreamProjection<HouseholdLedger, Guid>
{
    public HouseholdLedgerProjection()
    {
        Identity<ExpenseRecorded>(e => e.HouseholdId);
        Identity<ExpenseVoided>(e => e.HouseholdId);
        Identity<SettlementRecorded>(e => e.HouseholdId);
    }

    public void Apply(ExpenseRecorded e, HouseholdLedger ledger)
    {
        var touched = Distribute(ledger, e.ExpenseGroupId, e.FundingSources, e.Allocations, multiplier: 1);
        MarkSettled(ledger, e.ExpenseGroupId, touched, e.OccurredAt);
    }

    // Voids appended before ExpenseVoided carried its group read it from the expense's read model;
    // an expense never changes group.
    public async Task Apply(ExpenseVoided e, HouseholdLedger ledger, IQuerySession session)
    {
        var groupId = e.ExpenseGroupId
            ?? (await session.LoadAsync<ExpenseReadModel>(e.ExpenseId))?.ExpenseGroupId;
        if (groupId is not { } group) return;
        var touched = Distribute(ledger, group, e.FundingSources, e.Allocations, multiplier: -1);
        MarkSettled(ledger, group, touched, e.OccurredAt);
    }

    public void Apply(SettlementRecorded e, HouseholdLedger ledger)
    {
        if (e.ExpenseGroupId is not { } groupId) return;
        var (u1, u2, sign) = Order(e.PayerId, e.RecipientId);
        GetOrAdd(ledger, groupId, u1, u2).Cents += sign * e.Cents;
        MarkSettled(ledger, groupId, touched: true, e.OccurredAt);
    }

    // Markers record the moment a group (or the whole household) reached zero so timelines can collapse
    // everything up to it. An event that moved no pair leaves them alone.
    private static void MarkSettled(HouseholdLedger ledger, Guid groupId, bool touched, DateTimeOffset occurredAt)
    {
        if (!touched) return;

        // Markers only move forward: imports apply historical OccurredAt after newer events.
        if (ledger.Pairs.Where(p => p.GroupId == groupId).All(p => p.Cents == 0)
            && (!ledger.GroupSettledAt.TryGetValue(groupId, out var groupAt) || occurredAt >= groupAt))
            ledger.GroupSettledAt[groupId] = occurredAt;

        if (ledger.Pairs.GroupBy(p => (p.UserId1, p.UserId2)).All(g => g.Sum(p => p.Cents) == 0))
        {
            if (ledger.HouseholdSettledAt is { } householdAt && occurredAt < householdAt) return;
            if (ledger.HouseholdSettledAt != occurredAt) ledger.HouseholdSettledBefore = ledger.HouseholdSettledAt;
            ledger.HouseholdSettledAt = occurredAt;
        }
        else if (ledger.HouseholdSettledAt == occurredAt)
        {
            // The legs of one household settle share a timestamp; an intermediate zero that a later leg leaves again was never a settled state.
            ledger.HouseholdSettledAt = ledger.HouseholdSettledBefore;
        }
    }

    private static bool Distribute(
        HouseholdLedger ledger,
        Guid groupId,
        IReadOnlyList<FundingSource> fundingSources,
        IReadOnlyList<Allocation> allocations,
        int multiplier)
    {
        var net = LedgerMath.NetPerUser(fundingSources, allocations);

        var creditors = net.Where(kv => kv.Value > 0).ToList();
        var debtors = net.Where(kv => kv.Value < 0).ToList();
        var totalCredit = creditors.Sum(c => c.Value);
        var touched = false;

        foreach (var debtor in debtors)
        foreach (var creditor in creditors)
        {
            var share = (long)Math.Round((double)Math.Abs(debtor.Value) * creditor.Value / totalCredit);
            if (share == 0) continue;

            var (u1, u2, sign) = Order(creditor.Key, debtor.Key);
            GetOrAdd(ledger, groupId, u1, u2).Cents += multiplier * sign * share;
            touched = true;
        }

        return touched;
    }

    private static (Guid u1, Guid u2, int sign) Order(Guid creditor, Guid debtor)
    {
        var u1 = creditor < debtor ? creditor : debtor;
        var u2 = creditor < debtor ? debtor : creditor;
        return (u1, u2, creditor < debtor ? 1 : -1);
    }

    private static PairEntry GetOrAdd(HouseholdLedger ledger, Guid groupId, Guid u1, Guid u2)
    {
        var entry = ledger.Pairs.FirstOrDefault(p => p.GroupId == groupId && p.UserId1 == u1 && p.UserId2 == u2);
        if (entry is null)
        {
            entry = new PairEntry { GroupId = groupId, UserId1 = u1, UserId2 = u2 };
            ledger.Pairs.Add(entry);
        }
        return entry;
    }
}
