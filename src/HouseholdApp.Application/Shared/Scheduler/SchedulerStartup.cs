using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HouseholdApp.Application.Shared.Scheduler;

/// <summary>
/// Writes the app-wide jobs and drops every stored job that is not one of them, including the per-template jobs earlier
/// releases stored. Every node of a cluster does this as it starts; each write is an upsert under the store's lock, so they
/// converge on the same schedule.
/// </summary>
internal sealed partial class SchedulerStartup(
    ISchedulerFactory schedulers,
    IEnumerable<ScheduledJobDefinition> scheduledJobs,
    ILogger<SchedulerStartup> logger) : IHostedService
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

        var stored = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);
        var retired = stored
            .Where(k => k.Group != QuartzJobs.ScheduledGroup || !names.Contains(k.Name))
            .ToList();
        if (retired.Count > 0)
        {
            await scheduler.DeleteJobs(retired, cancellationToken);
            LogRetired(retired.Count, string.Join(", ", retired.Select(k => k.Group).Distinct()));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} retired scheduler jobs from groups: {Groups}")]
    private partial void LogRetired(int count, string groups);
}
