using HouseholdApp.Application.Modules.Expenses.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public enum ActivityKind { Added, Edited, Removed, Settlement, RecurringAdded, HouseholdSettlement, GroupDeleted, GroupRestored }

public sealed class ActivityEntry
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid ActorUserId { get; set; }
    public ActivityKind Kind { get; set; }
    public string Description { get; set; } = default!;
    public string? GroupName { get; set; }
    public Guid? GroupId { get; set; }
    public List<string> GroupNames { get; set; } = [];
    public long? SettlementCents { get; set; }
    public Guid? SettlementPayerId { get; set; }
    public Guid? SettlementRecipientId { get; set; }
    public Dictionary<Guid, long> ViewerDeltaCents { get; set; } = [];
}

// This is registered as an EventProjection ("one document per event"), distinct from
// ExpenseReadModelProjection (SingleStreamProjection — one evolving document per expense stream)
// and HouseholdLedgerProjection (MultiStreamProjection — one document per household aggregated
// across streams). Every ExpenseRecorded/ExpenseVoided/SettlementRecorded/ExpenseGroupDeleted/
// ExpenseGroupRestored event here produces its own permanent ActivityEntry row (a household settlement
// produces one, from its lead leg).
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
                // Voids appended before ExpenseVoided carried its group read it from the expense's read model.
                var voidedGroupId = voided.ExpenseGroupId
                    ?? (await operations.LoadAsync<ExpenseReadModel>(voided.ExpenseId, cancellation))?.ExpenseGroupId;
                var entry = MapVoided(e.Id, voided, voidedGroupId);
                if (entry is not null) operations.Store(entry);
                break;

            case SettlementRecorded { HouseholdSettlementId: { } householdSettlementId } leg:
                // The lead leg describes the whole household payment; the other legs add nothing to the feed.
                if (leg.SettlementId != householdSettlementId) break;
                var ids = leg.HouseholdSettlementGroupIds ?? [];
                var groups = ids.Count == 0
                    ? []
                    : await operations.LoadManyAsync<ExpenseGroupDocument>(cancellation, ids);
                var names = ids.Select(id => groups.FirstOrDefault(g => g.Id == id)?.Name).OfType<string>().ToList();
                operations.Store(MapHouseholdSettlement(e.Id, leg, names));
                break;

            case SettlementRecorded settlement:
                operations.Store(MapSettlement(e.Id, settlement));
                break;

            case ExpenseGroupDeleted deleted:
                operations.Store(MapGroupLifecycle(e.Id, ActivityKind.GroupDeleted, deleted.OccurredAt,
                    deleted.GroupId, deleted.HouseholdId, deleted.GroupName, deleted.PerformedByUserId));
                break;

            case ExpenseGroupRestored restored:
                operations.Store(MapGroupLifecycle(e.Id, ActivityKind.GroupRestored, restored.OccurredAt,
                    restored.GroupId, restored.HouseholdId, restored.GroupName, restored.PerformedByUserId));
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
            GroupId = e.ExpenseGroupId,
            ViewerDeltaCents = LedgerMath.NetPerUser(e.FundingSources, e.Allocations)
        };
    }

    public static ActivityEntry? MapVoided(Guid eventId, ExpenseVoided e, Guid? groupId)
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
            GroupId = groupId,
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
        GroupId = e.ExpenseGroupId,
        SettlementCents = e.Cents,
        SettlementPayerId = e.PayerId,
        SettlementRecipientId = e.RecipientId,
        ViewerDeltaCents = []
    };

    public static ActivityEntry MapHouseholdSettlement(Guid eventId, SettlementRecorded e, List<string> groupNames) => new()
    {
        Id = eventId,
        HouseholdId = e.HouseholdId,
        OccurredAt = e.OccurredAt,
        ActorUserId = e.PerformedByUserId,
        Kind = ActivityKind.HouseholdSettlement,
        Description = "",
        GroupNames = groupNames,
        SettlementCents = e.HouseholdSettlementCents ?? e.Cents,
        SettlementPayerId = e.PayerId,
        SettlementRecipientId = e.RecipientId,
        ViewerDeltaCents = []
    };

    public static ActivityEntry MapGroupLifecycle(
        Guid eventId, ActivityKind kind, DateTimeOffset occurredAt,
        Guid groupId, Guid householdId, string groupName, Guid actorId) => new()
    {
        Id = eventId,
        HouseholdId = householdId,
        OccurredAt = occurredAt,
        ActorUserId = actorId,
        Kind = kind,
        Description = "",
        GroupId = groupId,
        GroupName = groupName,
        ViewerDeltaCents = []
    };
}
