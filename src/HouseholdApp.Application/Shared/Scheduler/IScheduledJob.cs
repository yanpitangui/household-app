namespace HouseholdApp.Application.Shared.Scheduler;

/// <summary>An app-wide job that runs on a fixed schedule, registered with <see cref="ScheduledJobRegistration.AddScheduledJob{TJob}"/>.</summary>
public interface IScheduledJob
{
    Task RunAsync(CancellationToken ct);
}
