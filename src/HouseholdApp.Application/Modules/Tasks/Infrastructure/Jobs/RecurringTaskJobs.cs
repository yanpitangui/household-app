using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Shared.Scheduler;

namespace HouseholdApp.Application.Modules.Tasks.Infrastructure.Jobs;

/// <summary>Creates the Task of a RecurringTask each time its cron fires.</summary>
public sealed class RecurringTaskJobs(ITaskCommands taskCommands, ITaskRepository repository) : IRecurringJob
{
    public const string Name = "RecurringTask";

    public Task RunAsync(Guid entityId, CancellationToken ct) =>
        taskCommands.SpawnRecurringTaskAsync(entityId, ct);

    public Task<IReadOnlyDictionary<Guid, string>> GetActiveSchedulesAsync(CancellationToken ct) =>
        repository.GetActiveRecurringSchedulesAsync(ct);
}
