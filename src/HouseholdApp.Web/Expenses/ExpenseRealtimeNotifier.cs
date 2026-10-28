using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Events;
using StackExchange.Redis;

namespace HouseholdApp.Web.Expenses;

public sealed class ExpenseRealtimeNotifier(IConnectionMultiplexer redis, EventDispatchScope dispatchScope)
    : IEventHandler<ExpenseRecorded>,
      IEventHandler<ExpenseVoided>,
      IEventHandler<SettlementRecorded>,
      IEventHandler<ExpenseGroupDeleted>,
      IEventHandler<ExpenseGroupRestored>
{
    private Task Notify(Guid householdId) =>
        dispatchScope.SuppressNotifications
            ? Task.CompletedTask
            : redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal($"expense:{householdId}"),
                RedisValue.EmptyString,
                CommandFlags.FireAndForget);

    public Task HandleAsync(ExpenseRecorded evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(ExpenseVoided evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(SettlementRecorded evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(ExpenseGroupDeleted evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
    public Task HandleAsync(ExpenseGroupRestored evt, CancellationToken ct = default) => Notify(evt.HouseholdId);
}
