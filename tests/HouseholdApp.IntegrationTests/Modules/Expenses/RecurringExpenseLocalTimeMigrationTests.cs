using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Time;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(RecurringExpenseLocalTimeMigrationTests))]
public sealed class RecurringExpenseLocalTimeMigrationTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private readonly Guid _ana = Guid.NewGuid();
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString, "expenses_recurring_local_time_test");

    private RecurringExpenseLocalTimeMigration BuildSut() =>
        new(Store, new FakeTimeProvider(Now),
            new ServiceCollection()
                .AddSingleton<IHouseholdClock>(new FakeHouseholdClock(new FakeTimeProvider(Now), SaoPaulo))
                .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RecurringExpenseLocalTimeMigration>.Instance);

    // A template as it was stored before the household zone: its schedule reads in UTC.
    private async Task<Guid> SeedUtcTemplateAsync(DateTimeOffset firstOccurrence, bool ended = false)
    {
        var id = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        var schedule = new RecurrenceSchedule(
            RecurrenceFrequency.Monthly, DateOnly.FromDateTime(firstOccurrence.UtcDateTime), TimeOnly.FromDateTime(firstOccurrence.UtcDateTime));
        var funding = new[] { new FundingSource(_ana, 100_00) };
        var allocations = new[] { new Allocation(_ana, 100_00) };
        var created = new RecurringExpenseCreated(
            Guid.CreateVersion7(), firstOccurrence, id, householdId, Guid.NewGuid(), "Rent", funding, allocations,
            schedule, schedule.In(TimeZoneInfo.Utc).NextAfter(Now), _ana, Imported: true);
        await using var session = Store.LightweightSession();
        if (ended)
            session.Events.StartStream(id, created, new RecurringExpenseEnded(
                Guid.CreateVersion7(), Now, id, householdId, created.ExpenseGroupId, "Rent", _ana));
        else
            session.Events.StartStream(id, created);
        await session.SaveChangesAsync();
        return id;
    }

    private async Task<RecurringExpenseDocument> DocumentAsync(Guid id)
    {
        await using var qs = Store.QuerySession();
        return (await qs.LoadAsync<RecurringExpenseDocument>(id))!;
    }

    [Test]
    public async Task A_utc_template_reads_on_the_household_wall_clock_with_the_same_instant()
    {
        var id = await SeedUtcTemplateAsync(new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero));
        var before = (await DocumentAsync(id)).NextOccurrenceAt;

        await BuildSut().RunAsync(CancellationToken.None);

        var doc = await DocumentAsync(id);
        await Assert.That(doc.ScheduleIsLocal).IsTrue();
        await Assert.That(doc.Schedule).IsEqualTo(
            new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0)));
        await Assert.That(doc.NextOccurrenceAt).IsEqualTo(before);
    }

    [Test]
    public async Task A_template_whose_local_date_is_the_day_before_moves_its_date()
    {
        var id = await SeedUtcTemplateAsync(new DateTimeOffset(2026, 1, 5, 1, 0, 0, TimeSpan.Zero));

        await BuildSut().RunAsync(CancellationToken.None);

        await Assert.That((await DocumentAsync(id)).Schedule).IsEqualTo(
            new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 4), new TimeOnly(22, 0)));
    }

    [Test]
    public async Task An_ended_template_is_localized_too()
    {
        var id = await SeedUtcTemplateAsync(new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero), ended: true);

        await BuildSut().RunAsync(CancellationToken.None);

        var doc = await DocumentAsync(id);
        await Assert.That(doc.ScheduleIsLocal).IsTrue();
        await Assert.That(doc.Status).IsEqualTo(RecurringExpenseStatus.Ended);
    }

    [Test]
    public async Task Running_twice_appends_one_event_and_writes_no_activity()
    {
        var id = await SeedUtcTemplateAsync(new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero));

        await BuildSut().RunAsync(CancellationToken.None);
        await BuildSut().RunAsync(CancellationToken.None);

        await using var qs = Store.QuerySession();
        await Assert.That((await qs.Events.FetchStreamStateAsync(id))!.Version).IsEqualTo(2);
        await Assert.That(await qs.Query<ActivityEntry>().Where(a => a.RecurringExpenseId == id).ToListAsync()).IsEmpty();
    }
}
