using HouseholdApp.Application.Modules.Tasks.Domain;

namespace HouseholdApp.Application.Modules.Tasks.Application.Ports;

public interface ITaskRepository
{
    Task<Guid> SaveTaskAsync(HouseholdTask task, CancellationToken ct = default);
    Task<HouseholdTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default);
    Task SaveRecurringTaskAsync(RecurringTask task, CancellationToken ct = default);
    Task<RecurringTask?> GetRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default);


    /// <summary>Inserts the Task unless one with its id exists, and says whether it did.</summary>
    Task<bool> TryInsertTaskAsync(HouseholdTask task, CancellationToken ct = default);

    /// <summary>Ids of active RecurringTasks due at <paramref name="now"/>.</summary>
    Task<IReadOnlyList<Guid>> GetDueRecurringTaskIdsAsync(DateTimeOffset now, CancellationToken ct = default);
    Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default);
}
