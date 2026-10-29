using Microsoft.Extensions.Hosting;
using Quartz;

namespace HouseholdApp.Application.Shared.Scheduler;

/// <summary>
/// Writes the app-wide jobs, drops the ones no longer registered, and reconciles the recurring jobs. Every node of a
/// cluster does this as it starts; each write is an upsert under the store's lock, so they converge on the same schedule.
/// </summary>
internal sealed class SchedulerStartup(
    ISchedulerFactory schedulers,
    IEnumerable<ScheduledJobDefinition> scheduledJobs,
    RecurringJobReconciler reconciler) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulers.GetScheduler(cancellationToken);

        var names = new HashSet<string>();
        foreach (var job in scheduledJobs)
        {
            await QuartzJobs.UpsertCronAsync<ScheduledJobRunner>(scheduler, QuartzJobs.ScheduledKey(job.Name), job.Cron, cancellationToken);
            names.Add(job.Name);
        }

        var stored = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(QuartzJobs.ScheduledGroup), cancellationToken);
        foreach (var key in stored.Where(k => !names.Contains(k.Name)))
            await scheduler.DeleteJob(key, cancellationToken);

        await reconciler.ReconcileAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
