using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

// NextOccurrenceAt travels on the event because it depends on when the event happened, so a replay reads the same state.
public sealed record RecurringExpenseCreated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    IReadOnlyList<FundingSource> FundingSources,
    IReadOnlyList<Allocation> Allocations,
    RecurrenceSchedule Schedule,
    DateTimeOffset NextOccurrenceAt,
    Guid PerformedByUserId,
    // True for rows moved from the old table: they keep the template's history out of the feed and pushes.
    bool Imported = false) : IDomainEvent;

public sealed record RecurringExpenseUpdated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    IReadOnlyList<FundingSource> FundingSources,
    IReadOnlyList<Allocation> Allocations,
    RecurrenceSchedule Schedule,
    DateTimeOffset NextOccurrenceAt,
    long PreviousAmountCents,
    Guid PerformedByUserId) : IDomainEvent;

public sealed record RecurringExpenseOccurred(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    DateOnly OccurrenceDate,
    DateTimeOffset NextOccurrenceAt) : IDomainEvent;

public sealed record RecurringExpenseEnded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    Guid PerformedByUserId,
    bool Imported = false) : IDomainEvent;
