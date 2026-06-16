using System.Data;
using Dapper;
using HouseholdApp.Application.Modules.Households;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Valtuutus.Core;
using Valtuutus.Core.Data;
using Valtuutus.Data.Db;
using Valtuutus.Data.InMemory;

namespace HouseholdApp.IntegrationTests.Modules.Households;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerClass)]
public sealed class HouseholdCommandServiceTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScoped<MutableCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<MutableCurrentUser>());
        services.AddPersistence();
        services.AddFusionCache();
        services.AddHouseholdsModule();
        services.AddEventBus();
        services.AddInMemory();
        services.AddScoped<IDbDataWriterProvider, InMemoryDbWriterAdapter>();
        return services.BuildServiceProvider();
    }

    private AsyncServiceScope Scope(Guid userId)
    {
        var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().Id = userId;
        return scope;
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Test]
    public async Task AcceptInvitation_adds_second_user_as_member()
    {
        var ownerId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();

        Guid householdId;
        await using (var s = Scope(ownerId))
            householdId = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().CreateAsync("Test House");

        string token;
        await using (var s = Scope(ownerId))
            token = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().InviteAsync(householdId);

        bool accepted;
        await using (var s = Scope(joinerId))
            accepted = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync(token);

        await Assert.That(accepted).IsTrue();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var isMember = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM households.members WHERE household_id = @h AND user_id = @u)",
            new { h = householdId, u = joinerId });
        await Assert.That(isMember).IsTrue();
    }

    [Test]
    public async Task AcceptInvitation_returns_false_for_already_consumed_token()
    {
        var ownerId = Guid.NewGuid();
        var firstJoinerId = Guid.NewGuid();
        var secondJoinerId = Guid.NewGuid();

        Guid householdId;
        await using (var s = Scope(ownerId))
            householdId = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().CreateAsync("Test House 2");

        string token;
        await using (var s = Scope(ownerId))
            token = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().InviteAsync(householdId);

        bool firstAccepted;
        await using (var s = Scope(firstJoinerId))
            firstAccepted = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync(token);
        await Assert.That(firstAccepted).IsTrue();

        bool secondAccepted;
        await using (var s = Scope(secondJoinerId))
            secondAccepted = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync(token);
        await Assert.That(secondAccepted).IsFalse();
    }

    [Test]
    public async Task AcceptInvitation_returns_false_for_unknown_token()
    {
        bool accepted;
        await using (var s = Scope(Guid.NewGuid()))
            accepted = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync("no-such-token");
        await Assert.That(accepted).IsFalse();
    }
}

file sealed class InMemoryDbWriterAdapter(IDataWriterProvider inner) : IDbDataWriterProvider
{
    public Task<SnapToken> Write(IEnumerable<RelationTuple> relations, IEnumerable<AttributeTuple> attributes, CancellationToken ct)
        => inner.Write(relations, attributes, ct);

    public Task<SnapToken> Delete(DeleteFilter filter, CancellationToken ct)
        => inner.Delete(filter, ct);

    public Task<SnapToken> Write(IDbConnection connection, IEnumerable<RelationTuple> relations, IEnumerable<AttributeTuple> attributes, CancellationToken ct)
        => inner.Write(relations, attributes, ct);

    public Task<SnapToken> Write(IDbConnection connection, IDbTransaction transaction, IEnumerable<RelationTuple> relations, IEnumerable<AttributeTuple> attributes, CancellationToken ct)
        => inner.Write(relations, attributes, ct);

    public Task<SnapToken> Delete(IDbConnection connection, DeleteFilter filter, CancellationToken ct)
        => inner.Delete(filter, ct);

    public Task<SnapToken> Delete(IDbConnection connection, IDbTransaction transaction, DeleteFilter filter, CancellationToken ct)
        => inner.Delete(filter, ct);
}
