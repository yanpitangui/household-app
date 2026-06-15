using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Tasks.Domain;

public enum TaskStatus { Pending, Completed }

public sealed class HouseholdTask : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public Guid? AssignedTo { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public TaskStatus Status { get; private set; }
    public Guid? RecurringTaskId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private HouseholdTask() { }

    public static HouseholdTask Create(
        Guid householdId, string title, string? description,
        Guid? assignedTo, DateTimeOffset? dueDate, DateTimeOffset now,
        Guid? recurringTaskId = null)
    {
        var task = new HouseholdTask
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Title = title,
            Description = description,
            AssignedTo = assignedTo,
            DueDate = dueDate,
            Status = TaskStatus.Pending,
            RecurringTaskId = recurringTaskId,
            CreatedAt = now
        };
        task.Raise(new TaskCreated(Guid.NewGuid(), now, task.Id, householdId, title, assignedTo, dueDate, recurringTaskId));
        return task;
    }

    public void Assign(Guid userId, DateTimeOffset now)
    {
        AssignedTo = userId;
        Raise(new TaskAssigned(Guid.NewGuid(), now, Id, HouseholdId, userId));
    }

    public void Complete(Guid completedBy, DateTimeOffset now)
    {
        if (Status == TaskStatus.Completed)
            throw new InvalidOperationException("Task already completed.");
        Status = TaskStatus.Completed;
        Raise(new TaskCompleted(Guid.NewGuid(), now, Id, HouseholdId, completedBy));
    }
}
