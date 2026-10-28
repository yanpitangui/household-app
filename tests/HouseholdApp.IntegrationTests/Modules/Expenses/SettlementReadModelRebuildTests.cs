using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

// Own schema so the rebuild (which truncates the read-model table) runs isolated from the shared expenses schema.
[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class SettlementReadModelRebuildTests(PostgresFixture db)
{
    private IDocumentStore BuildStore() => DocumentStore.For(opts =>
    {
        opts.Connection(db.ConnectionString);
        opts.DatabaseSchemaName = "expenses_rebuild_test";
        opts.Events.AddEventType<SettlementRecorded>();
        opts.Projections.Add<SettlementReadModelProjection>(ProjectionLifecycle.Inline);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
    });

    [Test]
    public async Task Rebuild_restores_missing_settlement_documents_and_is_repeatable()
    {
        using var store = BuildStore();
        var settlementId = Guid.NewGuid();
        await using (var s = store.LightweightSession())
        {
            s.Events.Append(settlementId, new SettlementRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, settlementId, Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), 1234, DateTimeOffset.UtcNow, Guid.NewGuid()));
            await s.SaveChangesAsync();
        }
        // Simulates a settlement appended before the projection was registered.
        await using (var s = store.LightweightSession())
        {
            s.Delete<SettlementReadModel>(settlementId);
            await s.SaveChangesAsync();
        }
        await using (var before = store.QuerySession())
            await Assert.That(await before.LoadAsync<SettlementReadModel>(settlementId)).IsNull();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<SettlementReadModelProjection>(CancellationToken.None);
        await daemon.RebuildProjectionAsync<SettlementReadModelProjection>(CancellationToken.None);

        await using var qs = store.QuerySession();
        var doc = await qs.LoadAsync<SettlementReadModel>(settlementId);
        await Assert.That(doc).IsNotNull();
        await Assert.That(doc!.Cents).IsEqualTo(1234L);
    }
}
