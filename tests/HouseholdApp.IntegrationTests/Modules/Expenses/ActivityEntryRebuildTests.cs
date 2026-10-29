using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Npgsql;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

// Own schema so the rebuild (which truncates the activity table) runs isolated from the shared expenses schema.
[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ActivityEntryRebuildTests(PostgresFixture db)
{
    // Stands in for an event type a later deployment removed (e.g. ExpenseGroupCreated) whose rows stay in the store.
    public sealed record RemovedEvent(Guid Id);

    private IDocumentStore BuildStore(bool knowsRemovedEvent) => DocumentStore.For(opts =>
    {
        opts.Connection(db.ConnectionString);
        opts.DatabaseSchemaName = "expenses_activity_rebuild_test";
        opts.Events.AddEventType<ExpenseRecorded>();
        opts.Events.AddEventType<ExpenseVoided>();
        opts.Events.AddEventType<SettlementRecorded>();
        opts.Events.AddEventType<ExpenseGroupDeleted>();
        opts.Events.AddEventType<ExpenseGroupRestored>();
        opts.Events.AddEventType<ExpenseGroupUpdated>();
        if (knowsRemovedEvent) opts.Events.AddEventType<RemovedEvent>();
        opts.Projections.Add<ActivityEntryProjection>(ProjectionLifecycle.Inline);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
    });

    [Test]
    public async Task Rebuild_skips_events_of_types_the_deployment_no_longer_knows()
    {
        var settlementId = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        using (var oldDeployment = BuildStore(knowsRemovedEvent: true))
        await using (var s = oldDeployment.LightweightSession())
        {
            s.Events.Append(Guid.NewGuid(), new RemovedEvent(Guid.NewGuid()));
            s.Events.Append(settlementId, new SettlementRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, settlementId, householdId,
                Guid.NewGuid(), Guid.NewGuid(), 1234, DateTimeOffset.UtcNow, Guid.NewGuid()));
            await s.SaveChangesAsync();
        }
        // The removed type's assembly-qualified name no longer resolves either.
        await using (var conn = new NpgsqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE expenses_activity_rebuild_test.mt_events SET mt_dotnet_type = 'Gone.RemovedEvent, Gone' WHERE type = 'removed_event'",
                conn);
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = BuildStore(knowsRemovedEvent: false);
        using var daemon = await store.BuildProjectionDaemonAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await daemon.RebuildProjectionAsync<ActivityEntryProjection>(timeout.Token);

        await using var qs = store.QuerySession();
        var entries = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].SettlementCents).IsEqualTo(1234L);
    }
}
