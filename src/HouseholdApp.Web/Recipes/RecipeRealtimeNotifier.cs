using HouseholdApp.Application.Modules.Recipes.Domain;
using HouseholdApp.Application.Shared.Events;
using StackExchange.Redis;

namespace HouseholdApp.Web.Recipes;

public sealed class RecipeRealtimeNotifier(IConnectionMultiplexer redis)
    : IEventHandler<RecipeCreated>,
      IEventHandler<RecipeDeleted>
{
    private Task Notify(Guid householdId) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"recipe:{householdId}"),
            RedisValue.EmptyString,
            CommandFlags.FireAndForget);

    public Task HandleAsync(RecipeCreated evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(RecipeDeleted evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
}
