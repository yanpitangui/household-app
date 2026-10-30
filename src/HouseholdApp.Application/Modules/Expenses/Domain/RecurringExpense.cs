using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

// Stored as integers in the projection document, so new members take new numbers.
public enum RecurringExpenseStatus { Active = 0, Ended = 1 }

/// <summary>
/// A template that creates one Expense per occurrence of its schedule. Event-sourced: each operation builds its event,
/// applies it and raises it, so replaying the stream (Create + Apply) rebuilds the same state.
/// </summary>
public sealed class RecurringExpense : AggregateRoot
{
    public const int MaxCatchUp = 100;

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ExpenseGroupId { get; private set; }
    public string Description { get; private set; } = default!;
    public IReadOnlyList<FundingSource> FundingSources { get; private set; } = [];
    public IReadOnlyList<Allocation> Allocations { get; private set; } = [];
    public RecurrenceSchedule Schedule { get; private set; } = default!;
    public DateTimeOffset NextOccurrenceAt { get; private set; }
    public RecurringExpenseStatus Status { get; private set; }

    private RecurringExpense() { }

    public static RecurringExpense Create(
        Guid householdId, Guid expenseGroupId, string description,
        IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations,
        RecurrenceSchedule schedule, bool catchUp, DateTimeOffset now, Guid performedByUserId, Guid? id = null)
    {
        Validate(fundingSources, allocations);

        var startsInThePast = schedule.FirstDate < DateOnly.FromDateTime(now.UtcDateTime);
        if (startsInThePast && catchUp && schedule.CountThrough(now) > MaxCatchUp)
            throw new InvalidOperationException($"A catch-up can create at most {MaxCatchUp} expenses.");

        var next = startsInThePast && !catchUp ? schedule.NextAfter(now) : schedule.At(0);
        var created = new RecurringExpenseCreated(
            Guid.CreateVersion7(), now, id ?? Guid.CreateVersion7(), householdId, expenseGroupId, description,
            fundingSources, allocations, schedule, next, performedByUserId);

        var template = Create(created);
        template.Raise(created);
        return template;
    }

    /// <summary>Dates due at <paramref name="now"/>, oldest first. Empty unless the template is active.</summary>
    public IReadOnlyList<DateOnly> DueOccurrences(DateTimeOffset now)
    {
        if (Status != RecurringExpenseStatus.Active) return [];
        var due = new List<DateOnly>();
        for (var at = NextOccurrenceAt; at <= now; at = Schedule.NextAfter(at))
            due.Add(DateOnly.FromDateTime(at.UtcDateTime));
        return due;
    }

    public Expense BuildExpense(DateOnly date, DateTimeOffset now, bool coveredByCreatedEvent) =>
        Expense.Record(
            HouseholdId, ExpenseGroupId, Description, new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero),
            FundingSources, Allocations, now, performedByUserId: FundingSources[0].UserId,
            id: RecurrenceSchedule.OccurrenceId(Id, date), recurringExpenseId: Id,
            coveredByPlanEvent: coveredByCreatedEvent);

    public void MarkOccurred(DateOnly date, DateTimeOffset now)
    {
        var next = Schedule.NextAfter(new DateTimeOffset(date, Schedule.Time, TimeSpan.Zero));
        var occurred = new RecurringExpenseOccurred(Guid.CreateVersion7(), now, Id, HouseholdId, date, next);
        Apply(occurred);
        Raise(occurred);
    }

    public void Update(
        string description, IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations,
        RecurrenceSchedule schedule, DateTimeOffset now, Guid performedByUserId)
    {
        EnsureActive();
        Validate(fundingSources, allocations);
        if (schedule.FirstDate != Schedule.FirstDate && schedule.FirstDate < DateOnly.FromDateTime(now.UtcDateTime))
            throw new InvalidOperationException("A changed first date must be today or later.");

        var next = schedule == Schedule ? NextOccurrenceAt : schedule.NextAfter(now);
        var updated = new RecurringExpenseUpdated(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, description, fundingSources, allocations,
            schedule, next, FundingSources.Sum(f => f.Cents), performedByUserId);
        Apply(updated);
        Raise(updated);
    }

    public void End(DateTimeOffset now, Guid performedByUserId)
    {
        EnsureActive();
        var ended = new RecurringExpenseEnded(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, Description, performedByUserId);
        Apply(ended);
        Raise(ended);
    }

    public static RecurringExpense Create(RecurringExpenseCreated e) => new()
    {
        Id = e.RecurringExpenseId,
        HouseholdId = e.HouseholdId,
        ExpenseGroupId = e.ExpenseGroupId,
        Description = e.Description,
        FundingSources = e.FundingSources,
        Allocations = e.Allocations,
        Schedule = e.Schedule,
        NextOccurrenceAt = e.NextOccurrenceAt,
        Status = RecurringExpenseStatus.Active
    };

    public void Apply(RecurringExpenseUpdated e)
    {
        Description = e.Description;
        FundingSources = e.FundingSources;
        Allocations = e.Allocations;
        Schedule = e.Schedule;
        NextOccurrenceAt = e.NextOccurrenceAt;
    }

    public void Apply(RecurringExpenseOccurred e) => NextOccurrenceAt = e.NextOccurrenceAt;
    public void Apply(RecurringExpenseEnded e) => Status = RecurringExpenseStatus.Ended;

    private void EnsureActive()
    {
        if (Status != RecurringExpenseStatus.Active)
            throw new InvalidOperationException("This recurring expense has ended.");
    }

    private static void Validate(IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations)
    {
        if (fundingSources.Count == 0 || allocations.Count == 0)
            throw new InvalidOperationException("A recurring expense needs a payer and at least one member.");
        if (fundingSources.Sum(f => f.Cents) <= 0)
            throw new InvalidOperationException("A recurring expense needs an amount above zero.");
        if (fundingSources.Sum(f => f.Cents) != allocations.Sum(a => a.Cents))
            throw new InvalidOperationException("Funding sources must equal allocations.");
    }
}
