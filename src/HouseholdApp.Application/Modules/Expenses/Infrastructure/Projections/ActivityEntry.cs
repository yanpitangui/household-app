using HouseholdApp.Application.Modules.Expenses.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public enum ActivityKind { Added, Edited, Removed, Settlement, RecurringAdded }

public sealed class ActivityEntry
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid ActorUserId { get; set; }
    public ActivityKind Kind { get; set; }
    public string Description { get; set; } = default!;
    public string? GroupName { get; set; }
    public Guid? SettlementPayerId { get; set; }
    public Guid? SettlementRecipientId { get; set; }
    public Dictionary<Guid, long> ViewerDeltaCents { get; set; } = [];
}

// This is registered as an EventProjection ("one document per event"), distinct from
// ExpenseReadModelProjection (SingleStreamProjection — one evolving document per expense stream)
// and HouseholdLedgerProjection (MultiStreamProjection — one document per household aggregated
// across streams). Every ExpenseRecorded/ExpenseVoided/SettlementRecorded event here produces its
// own permanent ActivityEntry row.
//
// Marten 9.12.0's EventProjection (JasperFx.Events 2.18.1) does NOT expose constructor-registered
// Project<T>/ProjectAsync<T> delegate methods. Its conventional Project/Create/Transform method
// discovery requires a source-generated dispatcher (partial class + JasperFx.Events.SourceGenerator
// package), which isn't referenced anywhere in this repo. The supported, documented extension point
// without that package is overriding ApplyAsync directly (see JasperFxEventProjectionBase<,>.ApplyAsync
// XML doc: "Override this for explicit projection logic"), so that's what this class does — dispatch
// by event payload type, delegating to the (unit-testable, public, static) Map* methods below.
public sealed class ActivityEntryProjection : EventProjection
{
    public override async ValueTask ApplyAsync(IDocumentOperations operations, IEvent e, CancellationToken cancellation)
    {
        switch (e.Data)
        {
            case ExpenseRecorded recorded:
                var group = await operations.LoadAsync<ExpenseGroupDocument>(recorded.ExpenseGroupId, cancellation);
                operations.Store(MapRecorded(e.Id, recorded, group?.Name));
                break;

            case ExpenseVoided voided:
                var entry = MapVoided(e.Id, voided);
                if (entry is not null) operations.Store(entry);
                break;

            case SettlementRecorded settlement:
                operations.Store(MapSettlement(e.Id, settlement));
                break;

            default:
                // Intentional no-op: this projection only cares about the 3 event types above.
                break;
        }
    }

    public static ActivityEntry MapRecorded(Guid eventId, ExpenseRecorded e, string? groupName)
    {
        var kind = e.RecurringExpenseId is not null ? ActivityKind.RecurringAdded
            : e.CorrectedFromExpenseId is not null ? ActivityKind.Edited
            : ActivityKind.Added;

        return new ActivityEntry
        {
            Id = eventId,
            HouseholdId = e.HouseholdId,
            OccurredAt = e.OccurredAt,
            ActorUserId = e.PerformedByUserId,
            Kind = kind,
            Description = e.Description,
            GroupName = groupName,
            ViewerDeltaCents = LedgerMath.NetPerUser(e.FundingSources, e.Allocations)
        };
    }

    public static ActivityEntry? MapVoided(Guid eventId, ExpenseVoided e)
    {
        if (e.CorrectedByExpenseId is not null) return null;

        var net = LedgerMath.NetPerUser(e.FundingSources, e.Allocations);
        var reversed = net.ToDictionary(kv => kv.Key, kv => -kv.Value);

        return new ActivityEntry
        {
            Id = eventId,
            HouseholdId = e.HouseholdId,
            OccurredAt = e.OccurredAt,
            ActorUserId = e.PerformedByUserId,
            Kind = ActivityKind.Removed,
            Description = e.Description,
            GroupName = null,
            ViewerDeltaCents = reversed
        };
    }

    public static ActivityEntry MapSettlement(Guid eventId, SettlementRecorded e) => new()
    {
        Id = eventId,
        HouseholdId = e.HouseholdId,
        OccurredAt = e.OccurredAt,
        ActorUserId = e.PerformedByUserId,
        Kind = ActivityKind.Settlement,
        Description = "", // settlement text is composed from Payer/RecipientDisplayName in the UI layer
        SettlementPayerId = e.PayerId,
        SettlementRecipientId = e.RecipientId,
        ViewerDeltaCents = []
    };
}
