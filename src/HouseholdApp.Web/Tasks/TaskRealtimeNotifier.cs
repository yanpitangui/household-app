using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Shared.Events;
using StackExchange.Redis;

namespace HouseholdApp.Web.Tasks;

public sealed class TaskRealtimeNotifier(IConnectionMultiplexer redis)
    : IEventHandler<TaskCreated>,
      IEventHandler<TaskAssigned>,
      IEventHandler<TaskCompleted>,
      IEventHandler<TaskUncompleted>,
      IEventHandler<TaskDeleted>
{
    private Task Notify(Guid householdId) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"task:{householdId}"),
            RedisValue.EmptyString,
            CommandFlags.FireAndForget);

    public Task HandleAsync(TaskCreated evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(TaskAssigned evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(TaskCompleted evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(TaskUncompleted evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(TaskDeleted evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
}
