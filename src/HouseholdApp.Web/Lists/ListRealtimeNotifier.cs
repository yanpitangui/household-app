using HouseholdApp.Application.Modules.Lists.Domain;
using HouseholdApp.Application.Shared.Events;
using StackExchange.Redis;

namespace HouseholdApp.Web.Lists;

public sealed class ListRealtimeNotifier(IConnectionMultiplexer redis)
    : IEventHandler<ListItemAdded>,
      IEventHandler<ListItemCompleted>,
      IEventHandler<ListItemUncompleted>,
      IEventHandler<ListItemRemoved>,
      IEventHandler<ListItemCategoryChanged>
{
    private Task Notify(Guid listId) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"list:{listId}"),
            RedisValue.EmptyString,
            CommandFlags.FireAndForget);

    public Task HandleAsync(ListItemAdded evt, CancellationToken ct) => Notify(evt.ListId);
    public Task HandleAsync(ListItemCompleted evt, CancellationToken ct) => Notify(evt.ListId);
    public Task HandleAsync(ListItemUncompleted evt, CancellationToken ct) => Notify(evt.ListId);
    public Task HandleAsync(ListItemRemoved evt, CancellationToken ct) => Notify(evt.ListId);
    public Task HandleAsync(ListItemCategoryChanged evt, CancellationToken ct) => Notify(evt.ListId);
}
