using HouseholdApp.Application.Shared.Domain;
using Dapper;
using HouseholdApp.Application.Modules.Tasks.Application.Operations;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Tasks;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class TaskQueryServiceTests(PostgresFixture db)
{
    private readonly ITaskQueries _sut = new TaskQueryService(db.DataSource);

    [Test]
    public async Task ListAsync_returns_pending_tasks_by_default()
    {
        var householdId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO tasks.tasks (id, household_id, title, status) VALUES (@id, @householdId, 'Pending task', 0)",
            new { id = Guid.NewGuid(), householdId });
        await conn.ExecuteAsync(
            "INSERT INTO tasks.tasks (id, household_id, title, status) VALUES (@id, @householdId, 'Done task', 1)",
            new { id = Guid.NewGuid(), householdId });

        var result = await _sut.ListAsync(householdId, includeCompleted: false);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Title).IsEqualTo("Pending task");
        await Assert.That(result[0].Status).IsEqualTo("Pending");
    }

    [Test]
    public async Task ListAsync_includes_completed_when_requested()
    {
        var householdId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO tasks.tasks (id, household_id, title, status) VALUES (@id, @householdId, 'T1', 0)",
            new { id = Guid.NewGuid(), householdId });
        await conn.ExecuteAsync(
            "INSERT INTO tasks.tasks (id, household_id, title, status) VALUES (@id, @householdId, 'T2', 1)",
            new { id = Guid.NewGuid(), householdId });

        var result = await _sut.ListAsync(householdId, includeCompleted: true);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetAsync_returns_task_by_id()
    {
        var householdId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO tasks.tasks (id, household_id, title, description, status) VALUES (@id, @householdId, 'Fix pipe', 'Basement', 0)",
            new { id = taskId, householdId });

        var task = await _sut.GetAsync(taskId);

        await Assert.That(task).IsNotNull();
        await Assert.That(task!.Id).IsEqualTo(taskId);
        await Assert.That(task.Title).IsEqualTo("Fix pipe");
        await Assert.That(task.Description).IsEqualTo("Basement");
        await Assert.That(task.Status).IsEqualTo("Pending");
    }

    [Test]
    public async Task GetAsync_returns_null_for_unknown_task()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ListRecurringAsync_returns_recurring_tasks_for_household()
    {
        var householdId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO tasks.recurring_tasks (id, household_id, title, frequency, first_date, run_time, is_active) VALUES (@id, @householdId, 'Weekly clean', 'Weekly', '2026-11-09', '09:00', true)",
            new { id = Guid.NewGuid(), householdId });

        var result = await _sut.ListRecurringAsync(householdId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Title).IsEqualTo("Weekly clean");
        await Assert.That(result[0].Schedule).IsEqualTo(
            new RecurrenceSchedule(RecurrenceFrequency.Weekly, new DateOnly(2026, 11, 9), new TimeOnly(9, 0)));
        await Assert.That(result[0].IsActive).IsTrue();
    }

    [Test]
    public async Task Removing_a_member_unassigns_their_open_tasks_and_recurring_defaults()
    {
        var householdId = Guid.NewGuid();
        var bia = Guid.NewGuid();
        var leo = Guid.NewGuid();
        var openTask = Guid.NewGuid();
        var doneTask = Guid.NewGuid();
        var leosTask = Guid.NewGuid();
        await using (var conn = await db.DataSource.OpenConnectionAsync())
        {
            const string insertTask =
                "INSERT INTO tasks.tasks (id, household_id, title, assigned_to, status) VALUES (@id, @householdId, 'T', @assignee, @status)";
            await conn.ExecuteAsync(insertTask, new { id = openTask, householdId, assignee = bia, status = 0 });
            await conn.ExecuteAsync(insertTask, new { id = doneTask, householdId, assignee = bia, status = 1 });
            await conn.ExecuteAsync(insertTask, new { id = leosTask, householdId, assignee = leo, status = 0 });
            await conn.ExecuteAsync(
                "INSERT INTO tasks.recurring_tasks (id, household_id, title, default_assigned_to, frequency, first_date, run_time, is_active) VALUES (@id, @householdId, 'Weekly clean', @bia, 'Weekly', '2026-11-09', '09:00', true)",
                new { id = Guid.NewGuid(), householdId, bia });
        }

        await Assert.That(await _sut.CountOpenAssignedAsync(householdId, bia)).IsEqualTo(1);

        await using (var uow = new DapperUnitOfWork(db.DataSource, Substitute.For<IEventBus>()))
        {
            await uow.BeginTransactionAsync();
            await new TaskRepository(uow).UnassignMemberAsync(householdId, bia);
            await uow.CommitAsync();
        }

        await Assert.That((await _sut.GetAsync(openTask))!.AssignedTo).IsNull();
        // A completed task keeps the person who did it.
        await Assert.That((await _sut.GetAsync(doneTask))!.AssignedTo).IsEqualTo(bia);
        await Assert.That((await _sut.GetAsync(leosTask))!.AssignedTo).IsEqualTo(leo);
        await Assert.That((await _sut.ListRecurringAsync(householdId)).Single().DefaultAssignedTo).IsNull();
        await Assert.That(await _sut.CountOpenAssignedAsync(householdId, bia)).IsEqualTo(0);
    }
}
