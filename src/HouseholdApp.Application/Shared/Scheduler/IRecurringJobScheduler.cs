namespace HouseholdApp.Application.Shared.Scheduler;

/// <summary>
/// Keeps one schedule per entity for a recurring job registered with
/// <see cref="ScheduledJobRegistration.AddRecurringJob{TJob}"/>. A schedule is keyed by the job name and the entity id,
/// so scheduling again replaces it and no scheduler-side id has to be stored with the entity. Callers commit the entity
/// first and schedule it after, which is the order the reconciler relies on.
/// </summary>
public interface IRecurringJobScheduler
{
    /// <summary>Creates or replaces the entity's schedule. <paramref name="cronExpression"/> is a 5-field cron in UTC.</summary>
    Task ScheduleAsync(string jobName, Guid entityId, string cronExpression, CancellationToken ct = default);

    Task UnscheduleAsync(string jobName, Guid entityId, CancellationToken ct = default);

    /// <summary>Throws <see cref="FormatException"/> when <paramref name="cronExpression"/> is not one this scheduler can run.</summary>
    void Validate(string cronExpression);
}
