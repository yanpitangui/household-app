using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Time;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

// Stored as integers in the projection document, so new members take new numbers.
public enum RecurringExpenseStatus { Active = 0, Ended = 1, Paused = 2 }

/// <summary>
/// A template that creates one Expense per occurrence of its schedule, read on the household's wall clock (the zone
/// every operation receives). Event-sourced: each operation builds its event,
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
    public bool ScheduleIsLocal { get; private set; }
    public DateTimeOffset NextOccurrenceAt { get; private set; }
    public RecurringExpenseStatus Status { get; private set; }

    private RecurringExpense() { }

    public static RecurringExpense Create(
        Guid householdId, Guid expenseGroupId, string description,
        IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations,
        RecurrenceSchedule schedule, TimeZoneInfo zone, bool catchUp, DateTimeOffset now, Guid performedByUserId,
        Guid? id = null)
    {
        Validate(fundingSources, allocations);

        var zoned = schedule.In(zone);
        var startsInThePast = schedule.FirstDate < HouseholdTime.Today(zone, now);
        if (startsInThePast && catchUp && zoned.CountThrough(now) > MaxCatchUp)
            throw new InvalidOperationException($"A catch-up can create at most {MaxCatchUp} expenses.");

        var next = startsInThePast && !catchUp ? zoned.NextAfter(now) : zoned.At(0);
        var created = new RecurringExpenseCreated(
            Guid.CreateVersion7(), now, id ?? Guid.CreateVersion7(), householdId, expenseGroupId, description,
            fundingSources, allocations, schedule, next, performedByUserId, LocalTime: true);

        var template = Create(created);
        template.Raise(created);
        return template;
    }

    /// <summary>Dates due at <paramref name="now"/>, oldest first. Empty unless the template is active.</summary>
    public IReadOnlyList<DateOnly> DueOccurrences(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (Status != RecurringExpenseStatus.Active) return [];
        var zoned = Schedule.In(zone);
        var due = new List<DateOnly>();
        for (var at = NextOccurrenceAt; at <= now; at = zoned.NextAfterDateOf(at))
            due.Add(zoned.DateOf(at));
        return due;
    }

    public Expense BuildExpense(DateOnly date, DateTimeOffset now, bool coveredByCreatedEvent) =>
        Expense.Record(
            HouseholdId, ExpenseGroupId, Description, new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero),
            FundingSources, Allocations, now, performedByUserId: FundingSources[0].UserId,
            id: RecurrenceSchedule.OccurrenceId(Id, date), recurringExpenseId: Id,
            coveredByPlanEvent: coveredByCreatedEvent);

    public void MarkOccurred(DateOnly date, DateTimeOffset now, TimeZoneInfo zone, bool skipped = false)
    {
        var zoned = Schedule.In(zone);
        var next = zoned.NextAfter(zoned.AtDate(date));
        var occurred = new RecurringExpenseOccurred(Guid.CreateVersion7(), now, Id, HouseholdId, date, next, skipped);
        Apply(occurred);
        Raise(occurred);
    }

    public void Update(
        string description, IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations,
        RecurrenceSchedule schedule, TimeZoneInfo zone, DateTimeOffset now, Guid performedByUserId)
    {
        EnsureNotEnded();
        Validate(fundingSources, allocations);
        if (schedule.FirstDate != Schedule.FirstDate && schedule.FirstDate < HouseholdTime.Today(zone, now))
            throw new InvalidOperationException("A changed first date must be today or later.");

        // An untouched schedule still entered in UTC saves as its wall-clock equivalent, keeping its next instant.
        var unchanged = schedule == Schedule;
        if (unchanged && !ScheduleIsLocal) schedule = LocalSchedule(zone);
        var next = unchanged ? NextOccurrenceAt : schedule.In(zone).NextAfter(now);
        var updated = new RecurringExpenseUpdated(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, description, fundingSources, allocations,
            schedule, next, FundingSources.Sum(f => f.Cents), performedByUserId, LocalTime: true);
        Apply(updated);
        Raise(updated);
    }

    public void End(DateTimeOffset now, Guid performedByUserId)
    {
        EnsureNotEnded();
        var ended = new RecurringExpenseEnded(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, Description, performedByUserId);
        Apply(ended);
        Raise(ended);
    }

    /// <summary>Applies <see cref="MemberRemoval"/>; false when the template is ended or the member has no part in it.</summary>
    public bool RemoveMember(Guid userId, DateTimeOffset now, TimeZoneInfo zone, Guid performedByUserId)
    {
        if (Status == RecurringExpenseStatus.Ended) return false;
        switch (MemberRemoval.For(FundingSources, Allocations, userId))
        {
            case MemberRemoval.Ended:
                End(now, performedByUserId);
                return true;
            case MemberRemoval.Redistributed r:
                Update(Description, FundingSources, r.Allocations, Schedule, zone, now, performedByUserId);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Moves a schedule entered in UTC to <paramref name="zone"/>'s wall clock: the first occurrence keeps its instant, the
    /// ones after it follow the zone. False when the schedule is already local.
    /// </summary>
    public bool Localize(TimeZoneInfo zone, DateTimeOffset now)
    {
        if (ScheduleIsLocal) return false;
        var localized = new RecurringExpenseScheduleLocalized(Guid.CreateVersion7(), now, Id, HouseholdId, LocalSchedule(zone));
        Apply(localized);
        Raise(localized);
        return true;
    }

    // The UTC-entered schedule on zone's wall clock, its first occurrence at the same instant.
    private RecurrenceSchedule LocalSchedule(TimeZoneInfo zone)
    {
        var local = HouseholdTime.ToLocal(zone, Schedule.In(TimeZoneInfo.Utc).At(0)).DateTime;
        return new RecurrenceSchedule(Schedule.Frequency, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    public void Pause(DateTimeOffset now, Guid performedByUserId)
    {
        if (Status != RecurringExpenseStatus.Active)
            throw new InvalidOperationException(Status == RecurringExpenseStatus.Ended
                ? "This recurring expense has ended." : "This recurring expense is not active.");
        var paused = new RecurringExpensePaused(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, Description, performedByUserId);
        Apply(paused);
        Raise(paused);
    }

    public void Resume(DateTimeOffset now, TimeZoneInfo zone, Guid performedByUserId)
    {
        if (Status != RecurringExpenseStatus.Paused)
            throw new InvalidOperationException(Status == RecurringExpenseStatus.Ended
                ? "This recurring expense has ended." : "This recurring expense is not paused.");
        var resumed = new RecurringExpenseResumed(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, Description, Schedule.In(zone).NextAfter(now),
            performedByUserId);
        Apply(resumed);
        Raise(resumed);
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
        ScheduleIsLocal = e.LocalTime,
        NextOccurrenceAt = e.NextOccurrenceAt,
        Status = RecurringExpenseStatus.Active
    };

    public void Apply(RecurringExpenseUpdated e)
    {
        Description = e.Description;
        FundingSources = e.FundingSources;
        Allocations = e.Allocations;
        Schedule = e.Schedule;
        ScheduleIsLocal = e.LocalTime;
        NextOccurrenceAt = e.NextOccurrenceAt;
    }

    public void Apply(RecurringExpenseScheduleLocalized e)
    {
        Schedule = e.Schedule;
        ScheduleIsLocal = true;
    }

    public void Apply(RecurringExpenseOccurred e) => NextOccurrenceAt = e.NextOccurrenceAt;
    public void Apply(RecurringExpenseEnded e) => Status = RecurringExpenseStatus.Ended;

    public void Apply(RecurringExpensePaused e) => Status = RecurringExpenseStatus.Paused;

    public void Apply(RecurringExpenseResumed e)
    {
        Status = RecurringExpenseStatus.Active;
        NextOccurrenceAt = e.NextOccurrenceAt;
    }

    private void EnsureNotEnded()
    {
        if (Status == RecurringExpenseStatus.Ended)
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
