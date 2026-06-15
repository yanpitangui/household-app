using HouseholdApp.Application.Modules.Tasks.Domain;

namespace HouseholdApp.Application.Modules.Tasks.Application.Ports;

public interface ITaskRepository
{
    Task<Guid> SaveTaskAsync(HouseholdTask task, CancellationToken ct = default);
    Task<HouseholdTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default);
    Task SaveRecurringTaskAsync(RecurringTask task, CancellationToken ct = default);
    Task<RecurringTask?> GetRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default);
    Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default);
}
