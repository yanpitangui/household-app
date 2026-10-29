namespace HouseholdApp.Application.Shared.Scheduler;

/// <summary>
/// A job that runs once per entity on that entity's own cron, registered with
/// <see cref="ScheduledJobRegistration.AddRecurringJob{TJob}"/>. The module's data is the source of truth:
/// <see cref="GetActiveSchedulesAsync"/> is what the scheduler is reconciled against.
/// </summary>
public interface IRecurringJob
{
    Task RunAsync(Guid entityId, CancellationToken ct);

    /// <summary>Every entity that should have a schedule, with its 5-field cron (UTC).</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetActiveSchedulesAsync(CancellationToken ct);
}
