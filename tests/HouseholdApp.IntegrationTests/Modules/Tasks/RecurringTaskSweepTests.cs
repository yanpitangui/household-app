using HouseholdApp.Application.Modules.Tasks.Application.Operations;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Modules.Tasks.Infrastructure;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Tasks;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class RecurringTaskSweepTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _householdId = Guid.NewGuid();

    private static RecurrenceSchedule Weekly() =>
        new(RecurrenceFrequency.Weekly, new DateOnly(2026, 11, 12), new TimeOnly(9, 0));

    private async Task<T> WithSutAsync<T>(DateTimeOffset now, Func<TaskCommandService, Task<T>> action)
    {
        await using var uow = new DapperUnitOfWork(db.DataSource, Substitute.For<IEventBus>());
        var sut = new TaskCommandService(
            new TaskRepository(uow), uow, Substitute.For<IEventBus>(), new FakeTimeProvider(now), Substitute.For<ICurrentUser>());
        return await action(sut);
    }

    private Task<Guid> CreateAsync() => WithSutAsync(Now, sut =>
        sut.CreateRecurringTaskAsync(_householdId, "Water plants", null, null, Weekly()));

    private Task SpawnAsync(Guid id, DateTimeOffset now) => WithSutAsync(now, async sut =>
    {
        await sut.SpawnRecurringTaskAsync(id);
        return true;
    });

    private async Task<IReadOnlyList<TaskSummary>> TasksAsync() =>
        await new TaskQueryService(db.DataSource).ListAsync(_householdId);

    [Test]
    public async Task Spawn_creates_each_due_task_with_its_own_due_date_and_advances_the_cursor()
    {
        var id = await CreateAsync();

        await SpawnAsync(id, new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

        var tasks = await TasksAsync();
        await Assert.That(tasks.Select(t => t.DueDate!.Value.Day)).IsEquivalentTo(new[] { 12, 19 });
        var template = (await new TaskQueryService(db.DataSource).ListRecurringAsync(_householdId)).Single();
        await Assert.That(template.NextRunAt).IsEqualTo(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Spawn_again_with_nothing_due_creates_nothing()
    {
        var id = await CreateAsync();
        var at = new DateTimeOffset(2026, 11, 12, 9, 5, 0, TimeSpan.Zero);

        await SpawnAsync(id, at);
        await SpawnAsync(id, at.AddMinutes(5));

        await Assert.That((await TasksAsync()).Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_rerun_of_a_date_already_created_keeps_the_task_as_it_is()
    {
        var id = await CreateAsync();
        var at = new DateTimeOffset(2026, 11, 12, 9, 5, 0, TimeSpan.Zero);
        await SpawnAsync(id, at);
        var existing = (await TasksAsync()).Single();
        await WithSutAsync(at, async sut =>
        {
            await sut.CompleteTaskAsync(existing.Id);
            return true;
        });

        // Puts the cursor back, as a commit lost after the insert would leave it.
        await using (var conn = await db.DataSource.OpenConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE tasks.recurring_tasks SET next_run_at = '2026-11-12 09:00:00+00' WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        await SpawnAsync(id, at.AddMinutes(5));

        var query = new TaskQueryService(db.DataSource);
        await Assert.That((await query.GetAsync(existing.Id))!.Status).IsEqualTo("Completed");
        await Assert.That((await query.ListAsync(_householdId, includeCompleted: true)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Deactivate_rejects_another_households_template()
    {
        var id = await CreateAsync();

        await Assert.That(async () => await WithSutAsync(Now, async sut =>
            {
                await sut.DeactivateRecurringTaskAsync(Guid.NewGuid(), id);
                return true;
            }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_deactivate_that_races_a_spawn_in_progress_is_not_undone_by_it()
    {
        var id = await CreateAsync();
        await using var spawnUow = new DapperUnitOfWork(db.DataSource, Substitute.For<IEventBus>());
        var spawnRepo = new TaskRepository(spawnUow);
        await spawnUow.BeginTransactionAsync();
        var held = (await spawnRepo.GetRecurringTaskAsync(id))!;

        var deactivate = WithSutAsync(Now, async sut =>
        {
            await sut.DeactivateRecurringTaskAsync(_householdId, id);
            return true;
        });
        await Task.Delay(500);
        await Assert.That(deactivate.IsCompleted).IsFalse();

        await spawnRepo.SaveRecurringTaskAsync(held);
        await spawnUow.CommitAsync();
        await deactivate;

        var template = (await new TaskQueryService(db.DataSource).ListRecurringAsync(_householdId)).Single();
        await Assert.That(template.IsActive).IsFalse();
    }

    [Test]
    public async Task Due_ids_hold_the_active_templates_whose_time_has_come()
    {
        var due = await CreateAsync();
        var notYet = await WithSutAsync(Now, sut => sut.CreateRecurringTaskAsync(
            _householdId, "Later", null, null,
            new RecurrenceSchedule(RecurrenceFrequency.Weekly, new DateOnly(2026, 11, 30), new TimeOnly(9, 0))));
        var inactive = await CreateAsync();
        await WithSutAsync(Now, async sut =>
        {
            await sut.DeactivateRecurringTaskAsync(_householdId, inactive);
            return true;
        });

        await using var uow = new DapperUnitOfWork(db.DataSource, Substitute.For<IEventBus>());
        var ids = await new TaskRepository(uow).GetDueRecurringTaskIdsAsync(new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero));

        await Assert.That(ids).Contains(due);
        await Assert.That(ids).DoesNotContain(notYet);
        await Assert.That(ids).DoesNotContain(inactive);
    }
}
