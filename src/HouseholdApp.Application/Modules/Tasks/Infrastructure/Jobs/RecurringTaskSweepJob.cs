using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Shared.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Tasks.Infrastructure.Jobs;

/// <summary>
/// Every 5 minutes: creates the Tasks of the recurring templates that fell due. Each template runs in a scope of its own,
/// so its unit of work ends with it and a failed one leaves nothing behind for the ones after it.
/// </summary>
public sealed class RecurringTaskSweepJob(
    ITaskRepository repository,
    TimeProvider time,
    IServiceScopeFactory scopes,
    ILogger<RecurringTaskSweepJob> logger) : IScheduledJob
{
    public const string Name = "RecurringTaskSweep";
    public const string Cron = "*/5 * * * *";

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var id in await repository.GetDueRecurringTaskIdsAsync(time.GetUtcNow(), ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ITaskCommands>().SpawnRecurringTaskAsync(id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Sweeping recurring task {RecurringTaskId} failed", id);
            }
        }
    }
}
