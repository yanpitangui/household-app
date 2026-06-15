namespace HouseholdApp.Application.Shared.Scheduler;

public interface IRecurringJobScheduler
{
    Task<Guid> ScheduleCronAsync(string functionName, string cronExpression, Guid entityId, CancellationToken ct = default);
    Task UnscheduleCronAsync(Guid schedulerJobId, CancellationToken ct = default);
}
