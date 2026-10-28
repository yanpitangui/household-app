using HouseholdApp.Application.Modules.Expenses.Application.Import;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using Marten;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public sealed class ExpenseImportService(
    IDocumentSession session,
    IHouseholdQueries householdQueries,
    EventDispatchScope dispatchScope,
    TimeProvider time) : IExpenseImport
{
    public async Task<bool> GroupNameTakenAsync(Guid householdId, string groupName, CancellationToken ct = default)
    {
        var name = groupName.Trim();
        var existing = await session.Query<ExpenseGroupDocument>()
            .Where(g => g.HouseholdId == householdId && g.DeletedAt == null)
            .Select(g => g.Name)
            .ToListAsync(ct);
        return existing.Any(n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Guid> ImportAsync(
        Guid householdId, string groupName, ImportPlan plan, IReadOnlyList<Guid> memberForColumn,
        CancellationToken ct = default)
    {
        var name = groupName.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Group name is required.");
        if (!plan.IsBalanced)
            throw new InvalidOperationException("Import does not match the Splitwise total.");

        if (plan.People.Count != memberForColumn.Count)
            throw new InvalidOperationException("Every Splitwise person needs a household member.");
        var members = (await householdQueries.GetMembersAsync(householdId, ct)).Select(m => m.UserId).ToHashSet();
        if (SplitwiseImportPlanner.ValidateMapping(memberForColumn.Select(m => (Guid?)m).ToList(), members).Count > 0)
            throw new InvalidOperationException("Splitwise people must map to two different household members.");
        if (await GroupNameTakenAsync(householdId, name, ct))
            throw new InvalidOperationException($"An expense group named '{name}' already exists.");

        using var _ = dispatchScope.Suppress();

        // Every event is built before the first write so domain errors surface with nothing stored.
        var group = ExpenseGroup.Create(householdId, name, null, [], time.GetUtcNow());
        List<(Guid StreamId, object[] Events)> streams = [];
        foreach (var expense in plan.Expenses)
            streams.Add(BuildExpense(householdId, group.Id, expense, memberForColumn));
        foreach (var settlement in plan.Settlements)
            streams.Add(BuildSettlement(householdId, group.Id, settlement, memberForColumn));

        // Saved on its own first so ActivityEntryProjection can load the group name while
        // projecting the imported events.
        session.Store(new ExpenseGroupDocument { Id = group.Id, HouseholdId = householdId, Name = group.Name });
        await session.SaveChangesAsync(ct);

        try
        {
            foreach (var (streamId, events) in streams)
                session.Events.Append(streamId, events);
            // Runs to completion regardless of the request token so the commit outcome is never ambiguous.
            await session.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // Events still queued from the failed save reach only the suppressed notification handlers here.
            try
            {
                // A committed import keeps its group; only an import that wrote nothing is rolled back.
                var committed = streams.Count > 0
                    && await session.Events.FetchStreamStateAsync(streams[0].StreamId, CancellationToken.None) is not null;
                if (!committed)
                {
                    session.EjectAllPendingChanges();
                    session.Delete<ExpenseGroupDocument>(group.Id);
                    await session.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch
            {
                // Best effort: the original failure is what surfaces, and an empty group can be deleted from its group page.
            }
            throw;
        }

        return group.Id;
    }

    // OccurredAt is the row date at noon UTC plus the CSV line in milliseconds, so the day reads
    // correctly in any local time zone, same-day entries keep their file order and stay distinct
    // for feed paging; the actor mirrors the original payer.
    private static (Guid, object[]) BuildExpense(Guid householdId, Guid groupId, PlannedExpense planned, IReadOnlyList<Guid> members)
    {
        var date = ToTimestamp(planned.Date);
        var occurredAt = date.AddHours(12).AddMilliseconds(planned.LineNumber);
        var payer = members[planned.PayerColumn];
        var other = members[1 - planned.PayerColumn];

        List<Allocation> allocations = [];
        if (planned.PayerShareCents > 0) allocations.Add(new Allocation(payer, planned.PayerShareCents));
        if (planned.OtherShareCents > 0) allocations.Add(new Allocation(other, planned.OtherShareCents));

        var expense = Expense.Record(
            householdId, groupId, planned.Description, date,
            [new FundingSource(payer, planned.CostCents)], allocations,
            now: occurredAt, performedByUserId: payer, id: Guid.CreateVersion7(occurredAt));
        return (expense.Id, expense.DomainEvents.Cast<object>().ToArray());
    }

    private static (Guid, object[]) BuildSettlement(Guid householdId, Guid groupId, PlannedSettlement planned, IReadOnlyList<Guid> members)
    {
        var date = ToTimestamp(planned.Date);
        var occurredAt = date.AddHours(12).AddMilliseconds(planned.LineNumber);
        var payer = members[planned.PayerColumn];
        var settlementId = Guid.CreateVersion7(occurredAt);
        return (settlementId, [new SettlementRecorded(
            Guid.CreateVersion7(occurredAt), occurredAt, settlementId, householdId,
            payer, members[1 - planned.PayerColumn], planned.Cents, date,
            PerformedByUserId: payer, ExpenseGroupId: groupId)]);
    }

    private static DateTimeOffset ToTimestamp(DateOnly date) => new(date, TimeOnly.MinValue, TimeSpan.Zero);
}
