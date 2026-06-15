namespace HouseholdApp.Application.Modules.Tasks.Domain;

public sealed class RecurringTask
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public Guid? DefaultAssignedTo { get; private set; }
    public string CronExpression { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTimeOffset? NextRunAt { get; private set; }
    public Guid? SchedulerJobId { get; private set; }

    private RecurringTask() { }

    public static RecurringTask Create(
        Guid householdId, string title, string? description,
        Guid? defaultAssignedTo, string cronExpression, DateTimeOffset? nextRunAt) => new()
    {
        Id = Guid.NewGuid(),
        HouseholdId = householdId,
        Title = title,
        Description = description,
        DefaultAssignedTo = defaultAssignedTo,
        CronExpression = cronExpression,
        IsActive = true,
        NextRunAt = nextRunAt
    };

    public static RecurringTask Rehydrate(
        Guid id, Guid householdId, string title, string? description,
        Guid? defaultAssignedTo, string cronExpression,
        bool isActive, DateTimeOffset? nextRunAt, Guid? schedulerJobId) => new()
    {
        Id = id,
        HouseholdId = householdId,
        Title = title,
        Description = description,
        DefaultAssignedTo = defaultAssignedTo,
        CronExpression = cronExpression,
        IsActive = isActive,
        NextRunAt = nextRunAt,
        SchedulerJobId = schedulerJobId
    };

    public HouseholdTask Spawn(DateTimeOffset now) =>
        HouseholdTask.Create(HouseholdId, Title, Description, DefaultAssignedTo, NextRunAt, now, Id);

    public void UpdateNextRun(DateTimeOffset nextRunAt) => NextRunAt = nextRunAt;
    public void Deactivate() => IsActive = false;
    public void SetSchedulerJobId(Guid id) => SchedulerJobId = id;
}
