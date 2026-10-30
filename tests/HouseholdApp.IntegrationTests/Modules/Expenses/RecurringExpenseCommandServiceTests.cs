using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Marten.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

// Own schema + serial: the sweep reads every due template in its schema.
[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(RecurringExpenseCommandServiceTests))]
public sealed class RecurringExpenseCommandServiceTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _householdId = Guid.NewGuid();
    private readonly Guid _groupId = Guid.NewGuid();
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _leo = Guid.NewGuid();

    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString, "expenses_recurring_sweep_test");

    private RecurringExpenseCommandService BuildSut(IDocumentSession session, DateTimeOffset? now = null, IEventBus? bus = null)
    {
        bus ??= new RecurringRecordingBus();
        return new(
            session, new FakeCurrentUser(_ana), new FakeTimeProvider(now ?? Now), bus,
            new RecurringSessionFactory(Store, bus), NullLogger<RecurringExpenseCommandService>.Instance);
    }

    private static RecurrenceSchedule Monthly(int y, int m, int d) =>
        new(RecurrenceFrequency.Monthly, new DateOnly(y, m, d), new TimeOnly(9, 0));

    private async Task<Guid> CreateAsync(RecurrenceSchedule schedule, bool catchUp = false, DateTimeOffset? now = null)
    {
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, _groupId);
        await using var session = Store.LightweightSession();
        return await BuildSut(session, now).CreateAsync(
            _householdId, _groupId, "Rent", schedule,
            [new FundingSourceDto(_ana, 100_00)],
            [new AllocationDto(_ana, 50_00), new AllocationDto(_leo, 50_00)], catchUp);
    }

    private async Task SweepAsync(DateTimeOffset now)
    {
        await using var session = Store.LightweightSession();
        await BuildSut(session, now).RunSweepAsync();
    }

    private async Task<List<ExpenseReadModel>> ExpensesAsync(Guid templateId)
    {
        await using var qs = Store.QuerySession();
        return (await qs.Query<ExpenseReadModel>().Where(e => e.RecurringExpenseId == templateId).ToListAsync()).ToList();
    }

    private async Task<RecurringExpenseDocument> DocumentAsync(Guid templateId)
    {
        await using var qs = Store.QuerySession();
        return (await qs.LoadAsync<RecurringExpenseDocument>(templateId))!;
    }

    [Test]
    public async Task Create_with_catch_up_records_past_occurrences_in_the_same_transaction_and_hides_them_from_the_feed()
    {
        var bus = new RecurringRecordingBus();
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, _groupId);
        Guid id;
        await using (var session = Store.LightweightSession(new SessionOptions { Listeners = { new HouseholdApp.Application.Modules.Expenses.Infrastructure.ExpenseEventPublishingListener(bus) } }))
        {
            id = await BuildSut(session, bus: bus).CreateAsync(
                _householdId, _groupId, "Rent", Monthly(2026, 9, 1),
                [new FundingSourceDto(_ana, 100_00)], [new AllocationDto(_ana, 100_00)], catchUp: true);
        }

        var expenses = await ExpensesAsync(id);
        await Assert.That(expenses.Select(e => e.Date.Month).Order()).IsEquivalentTo(new[] { 9, 10, 11 });

        await using var qs = Store.QuerySession();
        var rows = await qs.Query<ActivityEntry>().Where(a => a.RecurringExpenseId == id).ToListAsync();
        await Assert.That(rows.Select(r => r.Kind)).IsEquivalentTo(new[] { ActivityKind.RecurringExpenseCreated });
        await Assert.That((await DocumentAsync(id)).NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Create_without_catch_up_records_nothing_for_a_past_first_date()
    {
        var id = await CreateAsync(Monthly(2026, 1, 15));

        await Assert.That(await ExpensesAsync(id)).IsEmpty();
    }

    [Test]
    public async Task Create_rejects_a_group_of_another_household()
    {
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, _groupId);
        await using var session = Store.LightweightSession();

        await Assert.That(async () => await BuildSut(session).CreateAsync(
                Guid.NewGuid(), _groupId, "Rent", Monthly(2026, 12, 1),
                [new FundingSourceDto(_ana, 100)], [new AllocationDto(_ana, 100)], false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Sweep_records_a_due_occurrence_once_and_advances()
    {
        var id = await CreateAsync(Monthly(2026, 11, 20));

        await SweepAsync(new DateTimeOffset(2026, 11, 20, 9, 5, 0, TimeSpan.Zero));
        await SweepAsync(new DateTimeOffset(2026, 11, 20, 9, 10, 0, TimeSpan.Zero));

        var expenses = await ExpensesAsync(id);
        await Assert.That(expenses.Count).IsEqualTo(1);
        await Assert.That(expenses[0].Date).IsEqualTo(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        await Assert.That((await DocumentAsync(id)).NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 20, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Sweep_catches_up_after_downtime()
    {
        var id = await CreateAsync(Monthly(2026, 11, 20));

        await SweepAsync(new DateTimeOffset(2027, 1, 25, 12, 0, 0, TimeSpan.Zero));

        await Assert.That((await ExpensesAsync(id)).Count).IsEqualTo(3);
    }

    [Test]
    public async Task Sweep_does_not_record_a_second_expense_when_an_edit_moves_todays_time()
    {
        var id = await CreateAsync(Monthly(2026, 11, 20));
        await SweepAsync(new DateTimeOffset(2026, 11, 20, 9, 5, 0, TimeSpan.Zero));

        var noon = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        await using (var session = Store.LightweightSession())
        {
            await BuildSut(session, noon).UpdateAsync(
                _householdId, id, "Rent", new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 20), new TimeOnly(18, 0)),
                [new FundingSourceDto(_ana, 100_00)], [new AllocationDto(_ana, 100_00)]);
        }
        await SweepAsync(new DateTimeOffset(2026, 11, 20, 18, 5, 0, TimeSpan.Zero));

        await Assert.That((await ExpensesAsync(id)).Count).IsEqualTo(1);
        await Assert.That((await DocumentAsync(id)).NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 20, 18, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Sweep_never_recreates_a_voided_expense()
    {
        var id = await CreateAsync(Monthly(2026, 11, 20));
        await SweepAsync(new DateTimeOffset(2026, 11, 20, 9, 5, 0, TimeSpan.Zero));
        var expenseId = (await ExpensesAsync(id)).Single().Id;
        await using (var session = Store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<Expense>(expenseId);
            stream.Aggregate!.Void("Mistake", Now, _ana);
            stream.AppendMany(stream.Aggregate.DomainEvents.ToArray());
            await session.SaveChangesAsync();
        }

        // Moving today's time puts the same date due again, so the sweep reaches the existing-stream check.
        await using (var session = Store.LightweightSession())
        {
            await BuildSut(session, new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)).UpdateAsync(
                _householdId, id, "Rent",
                new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 20), new TimeOnly(18, 0)),
                [new FundingSourceDto(_ana, 100_00)], [new AllocationDto(_ana, 50_00), new AllocationDto(_leo, 50_00)]);
        }
        await SweepAsync(new DateTimeOffset(2026, 11, 20, 18, 5, 0, TimeSpan.Zero));

        var expenses = await ExpensesAsync(id);
        await Assert.That(expenses.Count).IsEqualTo(1);
        await Assert.That(expenses[0].IsVoided).IsTrue();
        await Assert.That((await DocumentAsync(id)).NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 20, 18, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Sweep_waits_while_the_group_is_deleted_and_creates_what_was_missed_after_restore()
    {
        var id = await CreateAsync(Monthly(2026, 11, 20));
        await SetGroupDeletedAsync(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero));

        await SweepAsync(new DateTimeOffset(2026, 12, 21, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(await ExpensesAsync(id)).IsEmpty();

        await SetGroupDeletedAsync(null);
        await SweepAsync(new DateTimeOffset(2026, 12, 21, 0, 5, 0, TimeSpan.Zero));
        await Assert.That((await ExpensesAsync(id)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Sweep_skips_an_ended_template()
    {
        var id = await CreateAsync(Monthly(2026, 11, 20));
        await using (var session = Store.LightweightSession())
            await BuildSut(session).EndAsync(_householdId, id);

        await SweepAsync(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));

        await Assert.That(await ExpensesAsync(id)).IsEmpty();
    }

    [Test]
    public async Task Update_rejects_another_households_template()
    {
        var id = await CreateAsync(Monthly(2026, 12, 1));
        await using var session = Store.LightweightSession();
        var sut = BuildSut(session);

        await Assert.That(async () => await sut.UpdateAsync(
                Guid.NewGuid(), id, "Hacked", Monthly(2026, 12, 1),
                [new FundingSourceDto(_ana, 1)], [new AllocationDto(_ana, 1)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task End_rejects_another_households_template()
    {
        var id = await CreateAsync(Monthly(2026, 12, 1));
        await using var session = Store.LightweightSession();

        await Assert.That(async () => await BuildSut(session).EndAsync(Guid.NewGuid(), id))
            .Throws<InvalidOperationException>();
    }

    private async Task SetGroupDeletedAsync(DateTimeOffset? deletedAt)
    {
        await using var s = Store.LightweightSession();
        s.Store(new ExpenseGroupDocument { Id = _groupId, HouseholdId = _householdId, Name = "Group", DeletedAt = deletedAt });
        await s.SaveChangesAsync();
    }
}

file sealed class RecurringSessionFactory(IDocumentStore store, IEventBus bus) : ISessionFactory
{
    public IQuerySession QuerySession() => store.QuerySession();

    public IDocumentSession OpenSession() => store.LightweightSession(new SessionOptions
    {
        Timeout = 10,
        Listeners = { new HouseholdApp.Application.Modules.Expenses.Infrastructure.ExpenseEventPublishingListener(bus) }
    });
}

file sealed class RecurringRecordingBus : IEventBus
{
    public void Enqueue(IDomainEvent @event) { }
    public void Discard() { }
    public Task FlushTransactionalAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task FlushDeferredAsync(CancellationToken ct = default) => Task.CompletedTask;
}
