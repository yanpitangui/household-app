using Dapper;
using HouseholdApp.Application.Modules.Tasks.Application.Operations;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.IntegrationTests.Infrastructure;

namespace HouseholdApp.IntegrationTests.Modules.Tasks;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerClass)]
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
            "INSERT INTO tasks.recurring_tasks (id, household_id, title, cron_expression, is_active) VALUES (@id, @householdId, 'Weekly clean', '0 9 * * 1', true)",
            new { id = Guid.NewGuid(), householdId });

        var result = await _sut.ListRecurringAsync(householdId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Title).IsEqualTo("Weekly clean");
        await Assert.That(result[0].CronExpression).IsEqualTo("0 9 * * 1");
        await Assert.That(result[0].IsActive).IsTrue();
    }
}
