using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HouseholdApp.Application.Shared.Scheduler;

/// <summary>How the app's jobs are laid out in Quartz: keys, the runner each kind is stored with, and the upsert both use.</summary>
internal static class QuartzJobs
{
    /// <summary>Group of the app-wide jobs from <see cref="ScheduledJobRegistration.AddScheduledJob{TJob}"/>.</summary>
    public const string ScheduledGroup = "scheduled";

    /// <summary>A recurring job's group is the job's name and its key name is the entity id, so the store itself is the index.</summary>
    public static JobKey RecurringKey(string jobName, Guid entityId) => new(entityId.ToString(), jobName);

    public static JobKey ScheduledKey(string name) => new(name, ScheduledGroup);

    /// <summary>
    /// Stores <paramref name="key"/> with a single cron trigger of the same key, in UTC, and says whether anything was
    /// written. A trigger already on this cron is left as it is, so re-syncing keeps its next fire time and any misfire it
    /// is carrying. A missed fire runs once as soon as the scheduler is back, however many fires were missed.
    /// </summary>
    public static async Task<bool> UpsertCronAsync<TRunner>(IScheduler scheduler, JobKey key, string unixCron, CancellationToken ct)
        where TRunner : IJob
    {
        var quartzCron = QuartzCron.FromUnix(unixCron);
        var triggerKey = new TriggerKey(key.Name, key.Group);
        if (await scheduler.GetTrigger(triggerKey, ct) is ICronTrigger existing
            && existing.CronExpressionString == quartzCron
            && existing.TimeZone.Equals(TimeZoneInfo.Utc))
            return false;

        var job = JobBuilder.Create<TRunner>().WithIdentity(key).Build();
        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(job)
            .WithCronSchedule(quartzCron, cron => cron
                .InTimeZone(TimeZoneInfo.Utc)
                .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
            .Build();
        await scheduler.ScheduleJob(job, [trigger], ScheduleJobOptions.Replacing, ct);
        return true;
    }
}

// Every stored job names its runner type, so the two runners keep their names and namespace: moving or renaming one
// would leave each stored job pointing at a type that no longer exists.

/// <summary>
/// Quartz's entry point for every recurring job: the key says which job and which entity. A job whose name is not
/// registered on this node (one being retired, or one a newer node of a rolling deploy added) is skipped.
/// </summary>
[DisallowConcurrentExecution]
internal sealed partial class RecurringJobRunner(IServiceScopeFactory scopes, ILogger<RecurringJobRunner> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var key = context.JobDetail.Key;
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetKeyedService<IRecurringJob>(key.Group) is not { } job)
        {
            LogUnregistered(key.Group);
            return;
        }
        await job.RunAsync(Guid.Parse(key.Name), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped a run of recurring job {JobName}: not registered on this node")]
    private partial void LogUnregistered(string jobName);
}

/// <summary>Quartz's entry point for every app-wide job: the key name says which one. An unregistered one is skipped.</summary>
[DisallowConcurrentExecution]
internal sealed partial class ScheduledJobRunner(IServiceScopeFactory scopes, ILogger<ScheduledJobRunner> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var name = context.JobDetail.Key.Name;
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetKeyedService<IScheduledJob>(name) is not { } job)
        {
            LogUnregistered(name);
            return;
        }
        await job.RunAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped a run of scheduled job {JobName}: not registered on this node")]
    private partial void LogUnregistered(string jobName);
}

internal sealed class QuartzRecurringJobScheduler(ISchedulerFactory schedulers) : IRecurringJobScheduler
{
    public async Task ScheduleAsync(string jobName, Guid entityId, string cronExpression, CancellationToken ct = default)
    {
        var scheduler = await schedulers.GetScheduler(ct);
        await QuartzJobs.UpsertCronAsync<RecurringJobRunner>(scheduler, QuartzJobs.RecurringKey(jobName, entityId), cronExpression, ct);
    }

    public async Task UnscheduleAsync(string jobName, Guid entityId, CancellationToken ct = default)
    {
        var scheduler = await schedulers.GetScheduler(ct);
        await scheduler.DeleteJob(QuartzJobs.RecurringKey(jobName, entityId), ct);
    }

    public void Validate(string cronExpression) => QuartzCron.FromUnix(cronExpression);
}
