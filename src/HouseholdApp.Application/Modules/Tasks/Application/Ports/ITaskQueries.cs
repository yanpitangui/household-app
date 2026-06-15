namespace HouseholdApp.Application.Modules.Tasks.Application.Ports;

public sealed record TaskSummary(
    Guid Id, string Title, string? Description,
    Guid? AssignedTo, DateTimeOffset? DueDate, string Status, DateTimeOffset CreatedAt);

public sealed record RecurringTaskSummary(
    Guid Id, string Title, string? Description,
    Guid? DefaultAssignedTo, string CronExpression, bool IsActive, DateTimeOffset? NextRunAt);

public interface ITaskQueries
{
    Task<IReadOnlyList<TaskSummary>> ListAsync(Guid householdId, bool includeCompleted = false, CancellationToken ct = default);
    Task<TaskSummary?> GetAsync(Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<RecurringTaskSummary>> ListRecurringAsync(Guid householdId, CancellationToken ct = default);
}
