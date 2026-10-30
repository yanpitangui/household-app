using System.Text.Json;
using Dapper;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(RecurringExpenseStreamMigrationTests))]
public sealed class RecurringExpenseStreamMigrationTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _ana = Guid.NewGuid();
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString, "expenses_recurring_migration_test");

    private RecurringExpenseStreamMigration BuildSut() =>
        new(db.DataSource, Store, new FakeTimeProvider(Now), NullLogger<RecurringExpenseStreamMigration>.Instance);

    private async Task<Guid> InsertRowAsync(string frequency, DateTimeOffset startAt, bool isActive = true)
    {
        var id = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO expenses.recurring_expenses
                (id, household_id, expense_group_id, description, recurrence_frequency, start_at, cron_expression, is_active, funding_sources, allocations)
            VALUES
                (@Id, @HouseholdId, @GroupId, 'Rent', @Frequency, @StartAt, '0 9 1 * *', @IsActive, @Funding::jsonb, @Allocations::jsonb)
            """,
            new
            {
                Id = id, HouseholdId = Guid.NewGuid(), GroupId = Guid.NewGuid(), Frequency = frequency, StartAt = startAt,
                IsActive = isActive,
                Funding = JsonSerializer.Serialize(new[] { new FundingSource(_ana, 100_00) }),
                Allocations = JsonSerializer.Serialize(new[] { new Allocation(_ana, 100_00) })
            });
        return id;
    }

    private async Task<RecurringExpenseDocument?> DocumentAsync(Guid id)
    {
        await using var qs = Store.QuerySession();
        return await qs.LoadAsync<RecurringExpenseDocument>(id);
    }

    [Test]
    public async Task A_row_becomes_a_stream_with_its_id_and_no_backfill()
    {
        var id = await InsertRowAsync("Monthly", new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.Zero));

        await BuildSut().RunAsync(CancellationToken.None);

        var doc = await DocumentAsync(id);
        await Assert.That(doc!.Schedule).IsEqualTo(
            new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 31), new TimeOnly(9, 0)));
        await Assert.That(doc.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 30, 9, 0, 0, TimeSpan.Zero));
        await using var qs = Store.QuerySession();
        await Assert.That(await qs.Query<ExpenseReadModel>().Where(e => e.RecurringExpenseId == id).ToListAsync()).IsEmpty();
    }

    [Test]
    public async Task An_inactive_row_becomes_an_ended_template()
    {
        var id = await InsertRowAsync("Weekly", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), isActive: false);

        await BuildSut().RunAsync(CancellationToken.None);

        await Assert.That((await DocumentAsync(id))!.Status).IsEqualTo(RecurringExpenseStatus.Ended);
        await using var qs = Store.QuerySession();
        await Assert.That(await qs.Query<ActivityEntry>().Where(x => x.RecurringExpenseId == id).ToListAsync()).IsEmpty();
    }

    [Test]
    public async Task Running_twice_changes_nothing_and_writes_no_activity()
    {
        var id = await InsertRowAsync("Annually", new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        await BuildSut().RunAsync(CancellationToken.None);
        await BuildSut().RunAsync(CancellationToken.None);

        await using var qs = Store.QuerySession();
        var state = await qs.Events.FetchStreamStateAsync(id);
        await Assert.That(state!.Version).IsEqualTo(1);
        await Assert.That(await qs.Query<ActivityEntry>().Where(a => a.RecurringExpenseId == id).ToListAsync()).IsEmpty();
    }

    [Test]
    public async Task A_row_with_malformed_data_is_skipped_and_the_others_still_migrate()
    {
        var bad = await InsertRowAsync("Fortnightly-ish", new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        var good = await InsertRowAsync("Weekly", new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        await BuildSut().RunAsync(CancellationToken.None);

        await Assert.That(await DocumentAsync(bad)).IsNull();
        await Assert.That(await DocumentAsync(good)).IsNotNull();
    }
}
