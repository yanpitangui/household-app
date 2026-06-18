namespace HouseholdApp.Application.Modules.Tasks.Application.Ports;

public interface ITaskCommands
{
    Task<Guid> CreateTaskAsync(Guid householdId, string title, string? description,
        Guid? assignedTo, DateTimeOffset? dueDate, CancellationToken ct = default);

    Task AssignTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default);

    Task CompleteTaskAsync(Guid taskId, CancellationToken ct = default);
    Task UncompleteTaskAsync(Guid taskId, CancellationToken ct = default);

    Task<Guid> CreateRecurringTaskAsync(Guid householdId, string title, string? description,
        Guid? defaultAssignedTo, string cronExpression, CancellationToken ct = default);

    Task DeactivateRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default);

    Task SpawnRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default);

    Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default);
}
