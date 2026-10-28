using HouseholdApp.Application.Modules.Expenses.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Marten.Patching;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public enum ActivityKind { Added, Edited, Removed, Settlement, RecurringAdded, HouseholdSettlement, GroupDeleted, GroupRestored, GroupRenamed }

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
    public List<Guid> GroupIds { get; set; } = []; // parallel to GroupNames, so a rename can find its name
    public string? RenamedFrom { get; set; }
    public string? RenamedTo { get; set; }
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
// produces one, from its lead leg). An ExpenseGroupUpdated that changes the name produces a GroupRenamed
// row and refreshes the group name on the older rows.
//
// ApplyAsync is overridden directly (JasperFxEventProjectionBase<,>.ApplyAsync: "Override this for
// explicit projection logic") and dispatches by event payload type to the unit-testable static Map*
// methods below. The class is partial so the JasperFx source generator registers ActivityEntry as a
// published type, which lets Marten create its table at startup and clear it on rebuild.
public sealed partial class ActivityEntryProjection : EventProjection
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
                var named = ids.Select(id => groups.FirstOrDefault(g => g.Id == id)).OfType<ExpenseGroupDocument>()
                    .Select(g => (g.Id, g.Name)).ToList();
                operations.Store(MapHouseholdSettlement(e.Id, leg, named));
                break;

            case SettlementRecorded settlement:
                operations.Store(MapSettlement(e.Id, settlement));
                break;

            case ExpenseGroupUpdated updated when updated.OldName != updated.NewName:
                operations.Store(MapGroupRenamed(e.Id, updated));
                await ApplyRenameAsync(operations, updated, cancellation);
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

    // Older entries read the group's current name. Single-group entries are patched in the database, so a rebuild
    // also reaches entries stored earlier in the same batch; household settlements list several groups and are
    // rewritten one by one.
    private static async Task ApplyRenameAsync(
        IDocumentOperations operations, ExpenseGroupUpdated updated, CancellationToken cancellation)
    {
        operations.Patch<ActivityEntry>(a => a.GroupId == updated.GroupId && a.GroupName != null)
            .Set(a => a.GroupName, updated.NewName);

        var settlements = await operations.Query<ActivityEntry>()
            .Where(a => a.HouseholdId == updated.HouseholdId && a.Kind == ActivityKind.HouseholdSettlement
                && a.GroupIds.Contains(updated.GroupId))
            .ToListAsync(cancellation);
        foreach (var entry in settlements)
        {
            entry.GroupNames[entry.GroupIds.IndexOf(updated.GroupId)] = updated.NewName;
            operations.Store(entry);
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

    public static ActivityEntry MapHouseholdSettlement(
        Guid eventId, SettlementRecorded e, IReadOnlyList<(Guid Id, string Name)> groups) => new()
    {
        Id = eventId,
        HouseholdId = e.HouseholdId,
        OccurredAt = e.OccurredAt,
        ActorUserId = e.PerformedByUserId,
        Kind = ActivityKind.HouseholdSettlement,
        Description = "",
        GroupIds = groups.Select(g => g.Id).ToList(),
        GroupNames = groups.Select(g => g.Name).ToList(),
        SettlementCents = e.HouseholdSettlementCents ?? e.Cents,
        SettlementPayerId = e.PayerId,
        SettlementRecipientId = e.RecipientId,
        ViewerDeltaCents = []
    };

    public static ActivityEntry MapGroupRenamed(Guid eventId, ExpenseGroupUpdated e) => new()
    {
        Id = eventId,
        HouseholdId = e.HouseholdId,
        OccurredAt = e.OccurredAt,
        ActorUserId = e.PerformedByUserId,
        Kind = ActivityKind.GroupRenamed,
        Description = "",
        GroupId = e.GroupId,
        GroupName = e.NewName,
        RenamedFrom = e.OldName,
        RenamedTo = e.NewName,
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
