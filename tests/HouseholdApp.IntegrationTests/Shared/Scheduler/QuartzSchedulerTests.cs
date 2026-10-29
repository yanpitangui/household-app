using System.Collections.Concurrent;
using HouseholdApp.Application.Shared.Scheduler;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Quartz;

namespace HouseholdApp.IntegrationTests.Shared.Scheduler;

/// <summary>
/// The Quartz adapter against a real PostgreSQL store. Each test gets a database of its own, so the clustered nodes it
/// starts only ever see that test's schedule.
/// </summary>
[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class QuartzSchedulerTests(PostgresFixture db)
{
    private const string JobName = "Chores";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task Schedule_stores_a_utc_cron_trigger_under_the_entity_id()
    {
        await using var node = await StartNodeAsync(await NewDatabaseAsync(), new Templates());
        var entityId = Guid.NewGuid();

        await node.Jobs.ScheduleAsync(JobName, entityId, "0 9 * * 1");

        var trigger = await node.Scheduler.GetTrigger(TriggerKey(entityId)) as ICronTrigger;
        await Assert.That(trigger).IsNotNull();
        await Assert.That(trigger!.CronExpressionString).IsEqualTo("0 0 9 ? * MON");
        await Assert.That(trigger.TimeZone).IsEqualTo(TimeZoneInfo.Utc);
    }

    [Test]
    public async Task Schedule_again_keeps_a_trigger_on_the_same_cron_and_replaces_one_on_another()
    {
        await using var node = await StartNodeAsync(await NewDatabaseAsync(), new Templates());
        var entityId = Guid.NewGuid();
        await node.Jobs.ScheduleAsync(JobName, entityId, "0 9 * * 1");
        var first = await node.Scheduler.GetTrigger(TriggerKey(entityId));

        await Task.Delay(50);
        await node.Jobs.ScheduleAsync(JobName, entityId, "0 9 * * 1");
        var same = await node.Scheduler.GetTrigger(TriggerKey(entityId));
        await node.Jobs.ScheduleAsync(JobName, entityId, "0 9 1 * *");
        var replaced = await node.Scheduler.GetTrigger(TriggerKey(entityId)) as ICronTrigger;

        await Assert.That(same!.StartTimeUtc).IsEqualTo(first!.StartTimeUtc);
        await Assert.That(replaced!.CronExpressionString).IsEqualTo("0 0 9 1 * ?");
    }

    [Test]
    public async Task Unschedule_removes_the_job()
    {
        await using var node = await StartNodeAsync(await NewDatabaseAsync(), new Templates());
        var entityId = Guid.NewGuid();
        await node.Jobs.ScheduleAsync(JobName, entityId, "0 9 * * 1");

        await node.Jobs.UnscheduleAsync(JobName, entityId);

        await Assert.That(await node.Scheduler.Exists(QuartzJobs.RecurringKey(JobName, entityId))).IsFalse();
    }

    [Test]
    public async Task A_fired_job_runs_the_recurring_job_for_its_entity()
    {
        var templates = new Templates();
        await using var node = await StartNodeAsync(await NewDatabaseAsync(), templates);
        var entityId = Guid.NewGuid();
        await node.Jobs.ScheduleAsync(JobName, entityId, "0 9 * * 1");

        await node.Scheduler.TriggerJob(QuartzJobs.RecurringKey(JobName, entityId));

        await WaitUntilAsync(() => templates.Runs.Contains(entityId));
    }

    [Test]
    public async Task Startup_schedules_active_entities_and_removes_jobs_of_inactive_ones()
    {
        var connectionString = await NewDatabaseAsync();
        var (kept, missing, inactive) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var templates = new Templates();
        templates.Active[kept] = "0 9 * * 1";
        await using (var first = await StartNodeAsync(connectionString, templates))
        {
            await first.Jobs.ScheduleAsync(JobName, inactive, "0 9 * * 2");
        }

        templates.Active[missing] = "0 9 1 * *";
        await using var restarted = await StartNodeAsync(connectionString, templates);

        var keys = await restarted.Scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobName));
        await Assert.That(keys.Select(k => Guid.Parse(k.Name))).IsEquivalentTo([kept, missing]);
    }

    [Test]
    public async Task Startup_reschedules_an_entity_whose_cron_changed()
    {
        var connectionString = await NewDatabaseAsync();
        var entityId = Guid.NewGuid();
        var templates = new Templates();
        await using (var first = await StartNodeAsync(connectionString, templates))
        {
            await first.Jobs.ScheduleAsync(JobName, entityId, "0 9 * * 2");
        }

        templates.Active[entityId] = "0 9 * * 1";
        await using var restarted = await StartNodeAsync(connectionString, templates);

        var trigger = await restarted.Scheduler.GetTrigger(TriggerKey(entityId)) as ICronTrigger;
        await Assert.That(trigger!.CronExpressionString).IsEqualTo("0 0 9 ? * MON");
    }

    [Test]
    public async Task Startup_removes_the_jobs_of_a_recurring_job_no_longer_registered()
    {
        var connectionString = await NewDatabaseAsync();
        await using (var first = await StartNodeAsync(connectionString, new Templates()))
        {
            await QuartzJobs.UpsertCronAsync<RecurringJobRunner>(
                first.Scheduler, QuartzJobs.RecurringKey("Retired", Guid.NewGuid()), "0 9 * * 1", CancellationToken.None);
        }

        await using var restarted = await StartNodeAsync(connectionString, new Templates());

        await Assert.That(await restarted.Scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("Retired"))).IsEmpty();
    }

    [Test]
    public async Task Startup_writes_the_registered_app_wide_jobs_and_drops_retired_ones()
    {
        var connectionString = await NewDatabaseAsync();
        await using (var first = await StartNodeAsync(connectionString, new Templates()))
        {
            await QuartzJobs.UpsertCronAsync<ScheduledJobRunner>(
                first.Scheduler, QuartzJobs.ScheduledKey("Retired"), "0 3 * * *", CancellationToken.None);
        }

        await using var restarted = await StartNodeAsync(connectionString, new Templates());

        var keys = await restarted.Scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(QuartzJobs.ScheduledGroup));
        await Assert.That(keys.Select(k => k.Name)).IsEquivalentTo([RecurringJobReconcileJob.Name]);
    }

    [Test]
    public async Task Nodes_starting_together_on_one_database_join_one_cluster()
    {
        var connectionString = await NewDatabaseAsync();

        var nodes = await Task.WhenAll(
            StartNodeAsync(connectionString, new Templates()), StartNodeAsync(connectionString, new Templates()));
        await using var a = nodes[0];
        await using var b = nodes[1];

        var (metaA, metaB) = (await a.Scheduler.GetMetadata(), await b.Scheduler.GetMetadata());
        await Assert.That(metaA.JobStoreClustered).IsTrue();
        await Assert.That(metaB.SchedulerName).IsEqualTo(metaA.SchedulerName);
        await Assert.That(metaB.SchedulerInstanceId).IsNotEqualTo(metaA.SchedulerInstanceId);
    }

    private static TriggerKey TriggerKey(Guid entityId) => new(entityId.ToString(), JobName);

    private async Task<string> NewDatabaseAsync()
    {
        var name = $"scheduler_{Guid.NewGuid():N}";
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
        await create.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = name }.ConnectionString;
    }

    private static async Task<Node> StartNodeAsync(string connectionString, Templates templates)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(templates);
        builder.Services.AddScheduler(connectionString);
        builder.Services.AddRecurringJob<TemplateJob>(JobName);
        var host = builder.Build();
        await host.StartAsync();
        return new Node(host);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The scheduler did not get there in time.");
            await Task.Delay(100);
        }
    }

    private sealed class Node(IHost host) : IAsyncDisposable
    {
        public IRecurringJobScheduler Jobs { get; } = host.Services.GetRequiredService<IRecurringJobScheduler>();

        public IScheduler Scheduler { get; } =
            host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class Templates
    {
        public ConcurrentDictionary<Guid, string> Active { get; } = new();
        public ConcurrentQueue<Guid> Runs { get; } = new();
    }

    private sealed class TemplateJob(Templates templates) : IRecurringJob
    {
        public Task RunAsync(Guid entityId, CancellationToken ct)
        {
            templates.Runs.Enqueue(entityId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetActiveSchedulesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>(templates.Active));
    }
}
