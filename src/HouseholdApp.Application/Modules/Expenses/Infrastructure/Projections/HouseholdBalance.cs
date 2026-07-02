using HouseholdApp.Application.Modules.Expenses.Domain;
using Marten.Events.Projections;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public class PairEntry
{
    public Guid UserId1 { get; set; }
    public Guid UserId2 { get; set; }
    public long Cents { get; set; } // positive = UserId1 is owed by UserId2
}

public class HouseholdLedger
{
    public Guid Id { get; set; } // HouseholdId
    public List<PairEntry> Pairs { get; set; } = [];
}

public partial class HouseholdLedgerProjection : MultiStreamProjection<HouseholdLedger, Guid>
{
    public HouseholdLedgerProjection()
    {
        Identity<ExpenseRecorded>(e => e.HouseholdId);
        Identity<ExpenseVoided>(e => e.HouseholdId);
        Identity<SettlementRecorded>(e => e.HouseholdId);
    }

    public void Apply(ExpenseRecorded e, HouseholdLedger ledger) =>
        Distribute(ledger, e.FundingSources, e.Allocations, multiplier: 1);

    public void Apply(ExpenseVoided e, HouseholdLedger ledger) =>
        Distribute(ledger, e.FundingSources, e.Allocations, multiplier: -1);

    private static void Distribute(
        HouseholdLedger ledger,
        IReadOnlyList<FundingSource> fundingSources,
        IReadOnlyList<Allocation> allocations,
        int multiplier)
    {
        var net = new Dictionary<Guid, long>();
        foreach (var f in fundingSources) net[f.UserId] = net.GetValueOrDefault(f.UserId) + f.Cents;
        foreach (var a in allocations) net[a.UserId] = net.GetValueOrDefault(a.UserId) - a.Cents;

        var creditors = net.Where(kv => kv.Value > 0).ToList();
        var debtors = net.Where(kv => kv.Value < 0).ToList();
        var totalCredit = creditors.Sum(c => c.Value);

        foreach (var debtor in debtors)
        foreach (var creditor in creditors)
        {
            var share = (long)Math.Round((double)Math.Abs(debtor.Value) * creditor.Value / totalCredit);
            if (share == 0) continue;

            var (u1, u2, sign) = Order(creditor.Key, debtor.Key);
            GetOrAdd(ledger, u1, u2).Cents += multiplier * sign * share;
        }
    }

    public void Apply(SettlementRecorded e, HouseholdLedger ledger)
    {
        var (u1, u2, sign) = Order(e.PayerId, e.RecipientId);
        GetOrAdd(ledger, u1, u2).Cents += sign * e.Cents;
    }

    private static (Guid u1, Guid u2, int sign) Order(Guid creditor, Guid debtor)
    {
        var u1 = creditor < debtor ? creditor : debtor;
        var u2 = creditor < debtor ? debtor : creditor;
        return (u1, u2, creditor < debtor ? 1 : -1);
    }

    private static PairEntry GetOrAdd(HouseholdLedger ledger, Guid u1, Guid u2)
    {
        var entry = ledger.Pairs.FirstOrDefault(p => p.UserId1 == u1 && p.UserId2 == u2);
        if (entry is null)
        {
            entry = new PairEntry { UserId1 = u1, UserId2 = u2 };
            ledger.Pairs.Add(entry);
        }
        return entry;
    }
}
