using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Tasks.Domain;

public sealed class RecurringTask
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public Guid? DefaultAssignedTo { get; private set; }
    public RecurrenceSchedule Schedule { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTimeOffset NextRunAt { get; private set; }

    private RecurringTask() { }

    public static RecurringTask Create(
        Guid householdId, string title, string? description, Guid? defaultAssignedTo,
        RecurrenceSchedule schedule, DateTimeOffset now)
    {
        if (schedule.FirstDate < DateOnly.FromDateTime(now.UtcDateTime))
            throw new InvalidOperationException("The first date must be today or later.");

        return new RecurringTask
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            Title = title,
            Description = description,
            DefaultAssignedTo = defaultAssignedTo,
            Schedule = schedule,
            IsActive = true,
            NextRunAt = schedule.At(0)
        };
    }

    public static RecurringTask Rehydrate(
        Guid id, Guid householdId, string title, string? description, Guid? defaultAssignedTo,
        RecurrenceSchedule schedule, bool isActive, DateTimeOffset nextRunAt) => new()
    {
        Id = id,
        HouseholdId = householdId,
        Title = title,
        Description = description,
        DefaultAssignedTo = defaultAssignedTo,
        Schedule = schedule,
        IsActive = isActive,
        NextRunAt = nextRunAt
    };

    /// <summary>
    /// The Tasks due at <paramref name="now"/>, each due on its own occurrence date, and moves the cursor past them.
    /// Their ids are derived from the template and the date, so a retry produces the same ones.
    /// </summary>
    public IReadOnlyList<HouseholdTask> SpawnDue(DateTimeOffset now)
    {
        if (!IsActive) return [];
        var tasks = new List<HouseholdTask>();
        for (var at = NextRunAt; at <= now; at = Schedule.NextAfter(at))
        {
            var date = DateOnly.FromDateTime(at.UtcDateTime);
            tasks.Add(HouseholdTask.Create(
                HouseholdId, Title, Description, DefaultAssignedTo,
                new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero), now, Id,
                RecurrenceSchedule.OccurrenceId(Id, date)));
            NextRunAt = Schedule.NextAfter(at);
        }
        return tasks;
    }

    public void Deactivate() => IsActive = false;
}
