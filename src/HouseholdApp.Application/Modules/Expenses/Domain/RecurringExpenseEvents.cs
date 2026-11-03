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
    bool Imported = false,
    // True when Schedule's date and time are wall-clock in the household's zone (false: entered in UTC).
    bool LocalTime = false) : IDomainEvent;

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
    Guid PerformedByUserId,
    bool LocalTime = false) : IDomainEvent;

// Moved each template entered in UTC to the household's wall clock, keeping its instants; streams replay it.
public sealed record RecurringExpenseScheduleLocalized(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    RecurrenceSchedule Schedule) : IDomainEvent;

public sealed record RecurringExpenseOccurred(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    DateOnly OccurrenceDate,
    DateTimeOffset NextOccurrenceAt,
    // True when the date passed while the template's group was deleted, so no Expense was created for it.
    bool Skipped = false) : IDomainEvent;

public sealed record RecurringExpenseEnded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    Guid PerformedByUserId,
    bool Imported = false) : IDomainEvent;

public sealed record RecurringExpensePaused(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    Guid PerformedByUserId) : IDomainEvent;

// NextOccurrenceAt travels on the event, as on Created/Updated, so that replays reproduce the date chosen at resume time.
public sealed record RecurringExpenseResumed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid RecurringExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    DateTimeOffset NextOccurrenceAt,
    Guid PerformedByUserId) : IDomainEvent;
