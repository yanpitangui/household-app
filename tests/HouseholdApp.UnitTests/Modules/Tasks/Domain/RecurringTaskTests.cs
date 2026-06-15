using HouseholdApp.Application.Modules.Tasks.Domain;

namespace HouseholdApp.UnitTests.Modules.Tasks.Domain;

public sealed class RecurringTaskTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static RecurringTask Valid(string cron = "0 9 * * *") =>
        RecurringTask.Create(HouseholdId, "Weekly clean", null, UserId, cron, null);

    [Test]
    public async Task Create_generates_non_empty_id()
    {
        var task = Valid();

        await Assert.That(task.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Create_is_active_by_default()
    {
        var task = Valid();

        await Assert.That(task.IsActive).IsTrue();
    }

    [Test]
    public async Task Create_stores_cron_expression()
    {
        var task = Valid("0 0 1 * *");

        await Assert.That(task.CronExpression).IsEqualTo("0 0 1 * *");
    }

    [Test]
    public async Task Create_scheduler_job_id_is_null_by_default()
    {
        var task = Valid();

        await Assert.That(task.SchedulerJobId).IsNull();
    }

    [Test]
    public async Task SetSchedulerJobId_stores_id()
    {
        var task = Valid();
        var jobId = Guid.NewGuid();

        task.SetSchedulerJobId(jobId);

        await Assert.That(task.SchedulerJobId).IsEqualTo(jobId);
    }

    [Test]
    public async Task Deactivate_sets_is_active_false()
    {
        var task = Valid();

        task.Deactivate();

        await Assert.That(task.IsActive).IsFalse();
    }

    [Test]
    public async Task Spawn_creates_task_with_same_household()
    {
        var task = Valid();

        var spawned = task.Spawn(DateTimeOffset.UtcNow);

        await Assert.That(spawned.HouseholdId).IsEqualTo(HouseholdId);
    }

    [Test]
    public async Task Rehydrate_roundtrips_all_properties()
    {
        var id = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var nextRun = DateTimeOffset.UtcNow.AddHours(1);

        var task = RecurringTask.Rehydrate(
            id, HouseholdId, "Clean", "Desc", UserId, "0 9 * * *", false, nextRun, jobId);

        await Assert.That(task.Id).IsEqualTo(id);
        await Assert.That(task.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(task.IsActive).IsFalse();
        await Assert.That(task.SchedulerJobId).IsEqualTo(jobId);
        await Assert.That(task.NextRunAt).IsEqualTo(nextRun);
    }
}
