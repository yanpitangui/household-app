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
    Guid? CorrectedFromExpenseId = null) : IDomainEvent;

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
    Guid? CorrectedByExpenseId = null) : IDomainEvent;

// PerformedByUserId defaults for the same legacy-deserialization reason as ExpenseRecorded above.
public sealed record SettlementRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid SettlementId,
    Guid HouseholdId,
    Guid PayerId,
    Guid RecipientId,
    long Cents,
    DateTimeOffset Date,
    Guid PerformedByUserId = default) : IDomainEvent;
