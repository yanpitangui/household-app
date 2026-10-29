using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HouseholdApp.Application.Shared.Scheduler;

/// <summary>
/// Brings the scheduler in line with each recurring job's active entities: writes each entity's cron (a no-op when the
/// stored trigger already has it), removes jobs whose entity is gone or inactive, and removes the jobs of recurring jobs
/// no longer registered. Commands commit their entity before they schedule it, and the jobs are listed before the
/// entities are read, so a job is only ever removed once its entity's commit says so. A command racing a pass can leave
/// a stale schedule behind; the next pass reads the committed state again and corrects it.
/// </summary>
internal sealed partial class RecurringJobReconciler(
    ISchedulerFactory schedulers,
    IServiceScopeFactory scopes,
    IEnumerable<RecurringJobDefinition> definitions,
    ILogger<RecurringJobReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var scheduler = await schedulers.GetScheduler(ct);
        var names = definitions.Select(d => d.Name).ToHashSet();

        var retired = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct))
            .Where(k => k.Group != QuartzJobs.ScheduledGroup && !names.Contains(k.Group))
            .ToList();
        if (retired.Count > 0)
        {
            await scheduler.DeleteJobs(retired, ct);
            LogRetired(retired.Count, string.Join(", ", retired.Select(k => k.Group).Distinct()));
        }

        foreach (var name in names)
            await ReconcileAsync(scheduler, name, ct);
    }

    private async Task ReconcileAsync(IScheduler scheduler, string jobName, CancellationToken ct)
    {
        var scheduled = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(jobName), ct);

        await using var scope = scopes.CreateAsyncScope();
        var active = await scope.ServiceProvider.GetRequiredKeyedService<IRecurringJob>(jobName).GetActiveSchedulesAsync(ct);

        var removed = 0;
        foreach (var key in scheduled)
        {
            if (Guid.TryParse(key.Name, out var id) && active.ContainsKey(id)) continue;
            await scheduler.DeleteJob(key, ct);
            removed++;
        }

        var written = 0;
        foreach (var (entityId, cron) in active)
        {
            try
            {
                if (await QuartzJobs.UpsertCronAsync<RecurringJobRunner>(scheduler, QuartzJobs.RecurringKey(jobName, entityId), cron, ct))
                    written++;
            }
            catch (FormatException ex)
            {
                // One entity with a cron Quartz cannot run is reported and skipped, so it never holds up the rest.
                LogUnschedulable(ex, jobName, entityId, cron);
            }
        }

        if (written > 0 || removed > 0)
            LogReconciled(jobName, written, removed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciled recurring job {JobName}: {Written} scheduled or rescheduled, {Removed} removed")]
    private partial void LogReconciled(string jobName, int written, int removed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removed {Count} jobs of recurring jobs no longer registered: {Groups}")]
    private partial void LogRetired(int count, string groups);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recurring job {JobName} cannot schedule entity {EntityId} with cron '{Cron}'")]
    private partial void LogUnschedulable(Exception ex, string jobName, Guid entityId, string cron);
}

/// <summary>Hourly pass of <see cref="RecurringJobReconciler"/>, so a schedule a command failed to write heals without a restart.</summary>
internal sealed class RecurringJobReconcileJob(RecurringJobReconciler reconciler) : IScheduledJob
{
    public const string Name = "RecurringJobReconcile";
    public const string Cron = "17 * * * *";

    public Task RunAsync(CancellationToken ct) => reconciler.ReconcileAsync(ct);
}
