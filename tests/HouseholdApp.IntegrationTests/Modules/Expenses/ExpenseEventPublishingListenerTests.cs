using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Marten.Services;
using TUnit.Assertions.Enums;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

file sealed class RecordingEventBus : IEventBus
{
    public List<string> Calls { get; } = [];
    public List<IDomainEvent> Enqueued { get; } = [];
    public int TransactionalFlushCount { get; private set; }
    public int DeferredFlushCount { get; private set; }

    public void Enqueue(IDomainEvent @event)
    {
        Enqueued.Add(@event);
        Calls.Add("Enqueue");
    }
    public Task FlushTransactionalAsync(CancellationToken ct = default)
    {
        TransactionalFlushCount++;
        Calls.Add("FlushTransactional");
        return Task.CompletedTask;
    }
    public Task FlushDeferredAsync(CancellationToken ct = default)
    {
        DeferredFlushCount++;
        Calls.Add("FlushDeferred");
        return Task.CompletedTask;
    }
}

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseEventPublishingListenerTests(PostgresFixture db)
{
    [Test]
    public async Task Listener_flushes_exactly_once_per_commit_even_with_two_streams()
    {
        var bus = new RecordingEventBus();
        var store = ExpenseDocumentStore.For(db.ConnectionString);

        await using var session = store.LightweightSession(new SessionOptions
        {
            Listeners = { new ExpenseEventPublishingListener(bus) }
        });

        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var first = Expense.Record(householdId, groupId, "First", DateTimeOffset.UtcNow,
            [new FundingSource(userId, 500)], [new Allocation(userId, 500)], DateTimeOffset.UtcNow);
        var second = Expense.Record(householdId, groupId, "Second", DateTimeOffset.UtcNow,
            [new FundingSource(userId, 300)], [new Allocation(userId, 300)], DateTimeOffset.UtcNow);

        session.Events.Append(first.Id, first.DomainEvents.ToArray());
        session.Events.Append(second.Id, second.DomainEvents.ToArray());
        await session.SaveChangesAsync();

        await Assert.That(bus.TransactionalFlushCount).IsEqualTo(1);
        await Assert.That(bus.DeferredFlushCount).IsEqualTo(1);
        await Assert.That(bus.Enqueued.Count).IsEqualTo(2);
        await Assert.That(bus.Enqueued.OfType<ExpenseRecorded>().Count()).IsEqualTo(2);
        await Assert.That(bus.Calls.TakeLast(2)).IsEquivalentTo(["FlushTransactional", "FlushDeferred"], CollectionOrdering.Matching);
    }
}
