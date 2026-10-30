using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

public sealed record FundingSource(Guid UserId, long Cents);
public sealed record Allocation(Guid UserId, long Cents);

// PerformedByUserId/Description default so events persisted before this field existed still
// deserialize when AggregateStreamAsync replays old streams. Always pass a real value for new
// events — Expense.Record/Void enforce this at the call site; the default is legacy-JSON safety
// net only, not license to omit it.
public sealed record ExpenseRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    DateTimeOffset Date,
    IReadOnlyList<FundingSource> FundingSources,
    IReadOnlyList<Allocation> Allocations,
    Guid PerformedByUserId = default,
    Guid? RecurringExpenseId = null,
    Guid? CorrectedFromExpenseId = null,
    InstallmentLink? Installment = null,
    // True for Expenses a plan or recurring event already describes (catch-up, early payoff); the feed and pushes come from it.
    bool CoveredByPlanEvent = false) : IDomainEvent;

public sealed record ExpenseVoided(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ExpenseId,
    Guid HouseholdId,
    string? Reason,
    IReadOnlyList<FundingSource> FundingSources,
    IReadOnlyList<Allocation> Allocations,
    Guid PerformedByUserId = default,
    string Description = "",
    Guid? CorrectedByExpenseId = null,
    Guid? ExpenseGroupId = null,
    InstallmentLink? Installment = null,
    // True for the voids of a plan cancel; the cancel event describes them.
    bool CoveredByPlanEvent = false) : IDomainEvent;

// PerformedByUserId/ExpenseGroupId default for the same legacy-deserialization reason as ExpenseRecorded above.
// A household settle appends one leg per group, all sharing HouseholdSettlementId. Offset legs run recipient → payer
// and move no cash. The lead leg (SettlementId == HouseholdSettlementId) also carries the cash total and the
// groups involved, so the activity feed and push notifications describe the whole payment from one event.
public sealed record SettlementRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid SettlementId,
    Guid HouseholdId,
    Guid PayerId,
    Guid RecipientId,
    long Cents,
    DateTimeOffset Date,
    Guid PerformedByUserId = default,
    Guid? ExpenseGroupId = null,
    Guid? HouseholdSettlementId = null,
    bool IsOffset = false,
    long? HouseholdSettlementCents = null,
    IReadOnlyList<Guid>? HouseholdSettlementGroupIds = null) : IDomainEvent;

public sealed record ExpenseGroupDeleted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid GroupId,
    Guid HouseholdId,
    string GroupName,
    Guid PerformedByUserId) : IDomainEvent;

public sealed record ExpenseGroupRestored(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid GroupId,
    Guid HouseholdId,
    string GroupName,
    Guid PerformedByUserId) : IDomainEvent;

public sealed record ExpenseGroupUpdated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid GroupId,
    Guid HouseholdId,
    string OldName,
    string NewName,
    string? OldDescription,
    string? NewDescription,
    string OldIcon,
    string NewIcon,
    Guid PerformedByUserId) : IDomainEvent;
