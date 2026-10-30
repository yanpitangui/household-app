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

    [Test]
    public async Task Startup_removes_the_jobs_of_retired_per_template_schedules()
    {
        var connectionString = await NewDatabaseAsync();
        await using (var first = await StartNodeAsync(connectionString))
        {
            await QuartzJobs.UpsertCronAsync<RecurringJobRunner>(
                first.Scheduler, new JobKey(Guid.NewGuid().ToString(), "RecurringExpense"), "0 9 1 * *", CancellationToken.None);
        }

        await using var restarted = await StartNodeAsync(connectionString);

        await Assert.That(await restarted.Scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("RecurringExpense"))).IsEmpty();
    }

    [Test]
    public async Task Startup_writes_the_registered_app_wide_jobs_and_drops_retired_ones()
    {
        var connectionString = await NewDatabaseAsync();
        await using (var first = await StartNodeAsync(connectionString))
        {
            await QuartzJobs.UpsertCronAsync<ScheduledJobRunner>(
                first.Scheduler, QuartzJobs.ScheduledKey("Retired"), "0 3 * * *", CancellationToken.None);
        }

        await using var restarted = await StartNodeAsync(connectionString);

        var keys = await restarted.Scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(QuartzJobs.ScheduledGroup));
        await Assert.That(keys.Select(k => k.Name)).IsEquivalentTo([JobName]);
    }

    [Test]
    public async Task Nodes_starting_together_on_one_database_join_one_cluster()
    {
        var connectionString = await NewDatabaseAsync();

        var nodes = await Task.WhenAll(
            StartNodeAsync(connectionString), StartNodeAsync(connectionString));
        await using var a = nodes[0];
        await using var b = nodes[1];

        var (metaA, metaB) = (await a.Scheduler.GetMetadata(), await b.Scheduler.GetMetadata());
        await Assert.That(metaA.JobStoreClustered).IsTrue();
        await Assert.That(metaB.SchedulerName).IsEqualTo(metaA.SchedulerName);
        await Assert.That(metaB.SchedulerInstanceId).IsNotEqualTo(metaA.SchedulerInstanceId);
    }

    private async Task<string> NewDatabaseAsync()
    {
        var name = $"scheduler_{Guid.NewGuid():N}";
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
        await create.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = name }.ConnectionString;
    }

    private static async Task<Node> StartNodeAsync(string connectionString, Runs? runs = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(runs ?? new Runs());
        builder.Services.AddScheduler(connectionString);
        builder.Services.AddScheduledJob<CountingJob>(JobName, "* * * * *");
        var host = builder.Build();
        await host.StartAsync();
        return new Node(host);
    }

    private sealed class Node(IHost host) : IAsyncDisposable
    {
        public IScheduler Scheduler { get; } =
            host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class Runs { public int Count; }

    private sealed class CountingJob(Runs runs) : IScheduledJob
    {
        public Task RunAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref runs.Count);
            return Task.CompletedTask;
        }
    }
}
