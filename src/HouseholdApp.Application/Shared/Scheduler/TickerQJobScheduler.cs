using System.Text.Json;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace HouseholdApp.Application.Shared.Scheduler;

internal sealed class TickerQJobScheduler(ICronTickerManager<CronTickerEntity> cronManager) : IRecurringJobScheduler
{
    public async Task<Guid> ScheduleCronAsync(string functionName, string cronExpression, Guid entityId, CancellationToken ct = default)
    {
        var entity = new CronTickerEntity
        {
            Function = functionName,
            Expression = cronExpression,
            Request = JsonSerializer.SerializeToUtf8Bytes(entityId),
            IsEnabled = true
        };
        await cronManager.AddAsync(entity, ct);
        return entity.Id;
    }

    public Task UnscheduleCronAsync(Guid schedulerJobId, CancellationToken ct = default)
        => cronManager.DeleteAsync(schedulerJobId, ct);
}
