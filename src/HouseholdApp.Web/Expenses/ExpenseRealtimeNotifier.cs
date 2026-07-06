using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Events;
using StackExchange.Redis;

namespace HouseholdApp.Web.Expenses;

public sealed class ExpenseRealtimeNotifier(IConnectionMultiplexer redis)
    : IEventHandler<ExpenseRecorded>,
      IEventHandler<ExpenseVoided>,
      IEventHandler<SettlementRecorded>
{
    private Task Notify(Guid householdId) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"expense:{householdId}"),
            RedisValue.EmptyString,
            CommandFlags.FireAndForget);

    public Task HandleAsync(ExpenseRecorded evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(ExpenseVoided evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(SettlementRecorded evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
}
