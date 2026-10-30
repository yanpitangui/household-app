using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Marten.Patching;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public enum ActivityKind { Added, Edited, Removed, Settlement, RecurringAdded, HouseholdSettlement, GroupDeleted, GroupRestored, GroupRenamed,
    InstallmentPlanCreated, InstallmentAdded, InstallmentPlanPaidEarly, InstallmentPlanCancelled, InstallmentPlanEdited,
    RecurringExpenseCreated, RecurringExpenseEdited, RecurringExpenseEnded }

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
    // Installment plan rows; the plan page lists a plan's history by InstallmentPlanId.
    public Guid? InstallmentPlanId { get; set; }
    public int? InstallmentNumber { get; set; }
    public int? InstallmentCount { get; set; }
    public long? AmountCents { get; set; }
    public long? DownPaymentCents { get; set; } // set on a created row whose plan has a down payment
    public long? ReferenceCents { get; set; }
    public int? VoidedCount { get; set; }
    // Recurring expense rows; the template page lists a template's history by RecurringExpenseId.
    public Guid? RecurringExpenseId { get; set; }
    public RecurrenceFrequency? Frequency { get; set; }
}

// This is registered as an EventProjection ("one document per event"), distinct from
// ExpenseReadModelProjection (SingleStreamProjection — one evolving document per expense stream)
// and HouseholdLedgerProjection (MultiStreamProjection — one document per household aggregated
// across streams). Every ExpenseRecorded/ExpenseVoided/SettlementRecorded/ExpenseGroupDeleted/
// ExpenseGroupRestored event here produces its own permanent ActivityEntry row (a household settlement
// produces one, from its lead leg). The InstallmentPlanCreated/Updated/PaidEarly/Cancelled events each produce a
// plan row too; the Expenses a plan event already describes (CoveredByPlanEvent catch-ups, payoffs and cancel voids)
// stay out of the feed so the plan row is their single entry. The RecurringExpenseCreated/Updated/Ended events each
// produce a template row too (imported ones stay out); RecurringExpenseOccurred stays out, since the Expense it
// creates is its own row. An ExpenseGroupUpdated that changes the name produces a GroupRenamed
// row and refreshes the group name on the older rows.
//
// ApplyAsync is overridden directly (JasperFxEventProjectionBase<,>.ApplyAsync: "Override this for
// explicit projection logic") and dispatches by event payload type to the unit-testable static Map*
// methods below. The class is partial so the JasperFx source generator registers ActivityEntry as a
// published type, which lets Marten create its table at startup and clear it on rebuild.
public sealed partial class ActivityEntryProjection : EventProjection
{
    // The allow list keeps a rebuild to these events, so rows of event types a deployment removed stay unread.
    public ActivityEntryProjection()
    {
        IncludeType<ExpenseRecorded>();
        IncludeType<ExpenseVoided>();
        IncludeType<SettlementRecorded>();
        IncludeType<ExpenseGroupUpdated>();
        IncludeType<ExpenseGroupDeleted>();
        IncludeType<ExpenseGroupRestored>();
        IncludeType<InstallmentPlanCreated>();
        IncludeType<InstallmentPlanUpdated>();
        IncludeType<InstallmentPlanPaidEarly>();
        IncludeType<InstallmentPlanCancelled>();
        IncludeType<RecurringExpenseCreated>();
        IncludeType<RecurringExpenseUpdated>();
        IncludeType<RecurringExpenseEnded>();
    }

    public override async ValueTask ApplyAsync(IDocumentOperations operations, IEvent e, CancellationToken cancellation)
    {
        switch (e.Data)
        {
            case ExpenseRecorded recorded:
                var group = await operations.LoadAsync<ExpenseGroupDocument>(recorded.ExpenseGroupId, cancellation);
                var recordedEntry = MapRecorded(e.Id, recorded, group?.Name);
                if (recordedEntry is not null) operations.Store(recordedEntry);
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

            case InstallmentPlanCreated created:
                operations.Store(MapPlanCreated(e.Id, created, await GroupNameAsync(operations, created.ExpenseGroupId, cancellation)));
                break;

            case InstallmentPlanUpdated planUpdated:
                operations.Store(MapPlanEdited(e.Id, planUpdated, await GroupNameAsync(operations, planUpdated.ExpenseGroupId, cancellation)));
                break;

            case InstallmentPlanPaidEarly paidEarly:
                operations.Store(MapPlanPaidEarly(e.Id, paidEarly, await GroupNameAsync(operations, paidEarly.ExpenseGroupId, cancellation)));
                break;

            case InstallmentPlanCancelled planCancelled:
                operations.Store(MapPlanCancelled(e.Id, planCancelled, await GroupNameAsync(operations, planCancelled.ExpenseGroupId, cancellation)));
                break;

            case RecurringExpenseCreated recurringCreated:
                var createdEntry = MapRecurringCreated(e.Id, recurringCreated, await GroupNameAsync(operations, recurringCreated.ExpenseGroupId, cancellation));
                if (createdEntry is not null) operations.Store(createdEntry);
                break;

            case RecurringExpenseUpdated recurringUpdated:
                operations.Store(MapRecurringUpdated(e.Id, recurringUpdated, await GroupNameAsync(operations, recurringUpdated.ExpenseGroupId, cancellation)));
                break;

            case RecurringExpenseEnded { Imported: false } recurringEnded:
                operations.Store(MapRecurringEnded(e.Id, recurringEnded, await GroupNameAsync(operations, recurringEnded.ExpenseGroupId, cancellation)));
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

    private static async Task<string?> GroupNameAsync(IDocumentOperations operations, Guid groupId, CancellationToken ct) =>
        (await operations.LoadAsync<ExpenseGroupDocument>(groupId, ct))?.Name;

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

    public static ActivityEntry? MapRecorded(Guid eventId, ExpenseRecorded e, string? groupName)
    {
        if (e.CoveredByPlanEvent) return null;

        // An edited installment stays Edited; the plan tag below keeps it on the plan's history.
        var kind = e.CorrectedFromExpenseId is not null ? ActivityKind.Edited
            : e.RecurringExpenseId is not null ? ActivityKind.RecurringAdded
            : e.Installment is not null ? ActivityKind.InstallmentAdded
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
            ViewerDeltaCents = LedgerMath.NetPerUser(e.FundingSources, e.Allocations),
            InstallmentPlanId = e.Installment?.PlanId,
            InstallmentNumber = e.Installment?.Number,
            InstallmentCount = e.Installment?.Count,
            AmountCents = e.Installment is null ? null : e.FundingSources.Sum(f => f.Cents),
            RecurringExpenseId = e.RecurringExpenseId
        };
    }

    public static ActivityEntry? MapRecurringCreated(Guid eventId, RecurringExpenseCreated e, string? groupName) =>
        e.Imported
            ? null
            : RecurringEntry(eventId, ActivityKind.RecurringExpenseCreated, e.OccurredAt, e.HouseholdId, e.ExpenseGroupId,
                groupName, e.PerformedByUserId, e.Description, e.RecurringExpenseId, e.FundingSources.Sum(f => f.Cents),
                e.Schedule.Frequency);

    public static ActivityEntry MapRecurringUpdated(Guid eventId, RecurringExpenseUpdated e, string? groupName)
    {
        var entry = RecurringEntry(eventId, ActivityKind.RecurringExpenseEdited, e.OccurredAt, e.HouseholdId, e.ExpenseGroupId,
            groupName, e.PerformedByUserId, e.Description, e.RecurringExpenseId, e.FundingSources.Sum(f => f.Cents),
            e.Schedule.Frequency);
        entry.ReferenceCents = e.PreviousAmountCents;
        return entry;
    }

    public static ActivityEntry MapRecurringEnded(Guid eventId, RecurringExpenseEnded e, string? groupName) =>
        RecurringEntry(eventId, ActivityKind.RecurringExpenseEnded, e.OccurredAt, e.HouseholdId, e.ExpenseGroupId,
            groupName, e.PerformedByUserId, e.Description, e.RecurringExpenseId, null, null);

    private static ActivityEntry RecurringEntry(
        Guid eventId, ActivityKind kind, DateTimeOffset occurredAt, Guid householdId, Guid groupId, string? groupName,
        Guid actorId, string description, Guid templateId, long? amountCents, RecurrenceFrequency? frequency) => new()
    {
        Id = eventId,
        HouseholdId = householdId,
        OccurredAt = occurredAt,
        ActorUserId = actorId,
        Kind = kind,
        Description = description,
        GroupId = groupId,
        GroupName = groupName,
        RecurringExpenseId = templateId,
        AmountCents = amountCents,
        Frequency = frequency
    };

    public static ActivityEntry? MapVoided(Guid eventId, ExpenseVoided e, Guid? groupId)
    {
        if (e.CorrectedByExpenseId is not null || e.CoveredByPlanEvent) return null;

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
            ViewerDeltaCents = reversed,
            InstallmentPlanId = e.Installment?.PlanId,
            InstallmentNumber = e.Installment?.Number,
            InstallmentCount = e.Installment?.Count
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

    // The catch-up installments have no rows of their own, so the plan row carries their balance effect.
    public static ActivityEntry MapPlanCreated(Guid eventId, InstallmentPlanCreated e, string? groupName)
    {
        var terms = new InstallmentTerms(e.TotalCents, e.Count, e.DownPaymentCents, e.PurchaseDate, e.FirstDate);
        var segments = new[] { new SplitSegment(terms.FirstNumber, e.Allocations) };
        var delta = new Dictionary<Guid, long>();
        foreach (var n in terms.Numbers.Take(e.CatchUpCount))
        {
            var amount = terms.Amount(n);
            var net = LedgerMath.NetPerUser(
                [new FundingSource(e.PayerId, amount)],
                terms.Allocations(segments, n));
            foreach (var (user, cents) in net) delta[user] = delta.GetValueOrDefault(user) + cents;
        }

        var entry = PlanEntry(eventId, ActivityKind.InstallmentPlanCreated, e.OccurredAt, e.HouseholdId,
            e.ExpenseGroupId, groupName, e.PerformedByUserId, e.Description, e.PlanId, delta);
        entry.InstallmentCount = e.Count;
        entry.AmountCents = terms.Amount(1);
        entry.DownPaymentCents = terms.HasDownPayment ? terms.DownPaymentCents : null;
        return entry;
    }

    public static ActivityEntry MapPlanEdited(Guid eventId, InstallmentPlanUpdated e, string? groupName) =>
        PlanEntry(eventId, ActivityKind.InstallmentPlanEdited, e.OccurredAt, e.HouseholdId, e.ExpenseGroupId,
            groupName, e.PerformedByUserId, e.Description, e.PlanId, []);

    public static ActivityEntry MapPlanPaidEarly(Guid eventId, InstallmentPlanPaidEarly e, string? groupName)
    {
        var entry = PlanEntry(eventId, ActivityKind.InstallmentPlanPaidEarly, e.OccurredAt, e.HouseholdId,
            e.ExpenseGroupId, groupName, e.PerformedByUserId, e.Description, e.PlanId,
            LedgerMath.NetPerUser([new FundingSource(e.PayerId, e.PayoffCents)], e.Allocations));
        entry.InstallmentNumber = e.FromNumber;
        entry.InstallmentCount = e.Count;
        entry.AmountCents = e.PayoffCents;
        entry.ReferenceCents = e.RemainingCents;
        return entry;
    }

    // The voids of a cancel have no rows of their own, so the cancel row carries their reversal.
    public static ActivityEntry MapPlanCancelled(Guid eventId, InstallmentPlanCancelled e, string? groupName)
    {
        var delta = new Dictionary<Guid, long>();
        foreach (var v in e.VoidedInstallments)
            foreach (var (user, cents) in LedgerMath.NetPerUser(v.FundingSources, v.Allocations))
                delta[user] = delta.GetValueOrDefault(user) - cents;

        var entry = PlanEntry(eventId, ActivityKind.InstallmentPlanCancelled, e.OccurredAt, e.HouseholdId,
            e.ExpenseGroupId, groupName, e.PerformedByUserId, e.Description, e.PlanId, delta);
        entry.VoidedCount = e.VoidedInstallments.Count;
        return entry;
    }

    private static ActivityEntry PlanEntry(
        Guid eventId, ActivityKind kind, DateTimeOffset occurredAt, Guid householdId, Guid groupId, string? groupName,
        Guid actorId, string description, Guid planId, Dictionary<Guid, long> delta) => new()
    {
        Id = eventId,
        HouseholdId = householdId,
        OccurredAt = occurredAt,
        ActorUserId = actorId,
        Kind = kind,
        Description = description,
        GroupId = groupId,
        GroupName = groupName,
        InstallmentPlanId = planId,
        ViewerDeltaCents = delta
    };
}
