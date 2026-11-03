using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Domain;
using Marten.Events.Aggregation;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

/// <summary>Read side of a RecurringExpense: its page, Upcoming and the sweep read it.</summary>
public sealed class RecurringExpenseDocument
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid ExpenseGroupId { get; set; }
    public string Description { get; set; } = default!;
    public IReadOnlyList<FundingSource> FundingSources { get; set; } = [];
    public IReadOnlyList<Allocation> Allocations { get; set; } = [];
    public RecurrenceSchedule Schedule { get; set; } = default!;
    // False while the schedule still reads in UTC (before the localizing migration); the sweep leaves those alone.
    public bool ScheduleIsLocal { get; set; }
    public DateTimeOffset NextOccurrenceAt { get; set; }
    public RecurringExpenseStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public partial class RecurringExpenseProjection : SingleStreamProjection<RecurringExpenseDocument, Guid>
{
    public void Apply(RecurringExpenseCreated e, RecurringExpenseDocument doc)
    {
        doc.Id = e.RecurringExpenseId;
        doc.HouseholdId = e.HouseholdId;
        doc.ExpenseGroupId = e.ExpenseGroupId;
        doc.Description = e.Description;
        doc.FundingSources = e.FundingSources;
        doc.Allocations = e.Allocations;
        doc.Schedule = e.Schedule;
        doc.ScheduleIsLocal = e.LocalTime;
        doc.NextOccurrenceAt = e.NextOccurrenceAt;
        doc.Status = RecurringExpenseStatus.Active;
        doc.CreatedAt = e.OccurredAt;
    }

    public void Apply(RecurringExpenseUpdated e, RecurringExpenseDocument doc)
    {
        doc.Description = e.Description;
        doc.FundingSources = e.FundingSources;
        doc.Allocations = e.Allocations;
        doc.Schedule = e.Schedule;
        doc.ScheduleIsLocal = e.LocalTime;
        doc.NextOccurrenceAt = e.NextOccurrenceAt;
    }

    public void Apply(RecurringExpenseScheduleLocalized e, RecurringExpenseDocument doc)
    {
        doc.Schedule = e.Schedule;
        doc.ScheduleIsLocal = true;
    }

    public void Apply(RecurringExpenseOccurred e, RecurringExpenseDocument doc) =>
        doc.NextOccurrenceAt = e.NextOccurrenceAt;

    public void Apply(RecurringExpensePaused e, RecurringExpenseDocument doc) =>
        doc.Status = RecurringExpenseStatus.Paused;

    public void Apply(RecurringExpenseResumed e, RecurringExpenseDocument doc)
    {
        doc.Status = RecurringExpenseStatus.Active;
        doc.NextOccurrenceAt = e.NextOccurrenceAt;
    }

    public void Apply(RecurringExpenseEnded e, RecurringExpenseDocument doc) =>
        doc.Status = RecurringExpenseStatus.Ended;
}
