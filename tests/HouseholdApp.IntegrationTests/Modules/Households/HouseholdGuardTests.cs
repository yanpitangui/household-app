using HouseholdApp.Application.Modules.Households;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Valtuutus.Core.Configuration;
using Valtuutus.Data.Postgres;

namespace HouseholdApp.IntegrationTests.Modules.Households;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class HouseholdGuardTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var schemaStream = typeof(HouseholdsModule).Assembly
            .GetManifestResourceStream("HouseholdApp.Application.Shared.Authorization.schema.vtt")!;

        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScoped<MutableCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<MutableCurrentUser>());
        services.AddPersistence();
        services.AddHouseholdsModule();
        services.AddEventBus();
        services.AddValtuutusCore(schemaStream);
        services.AddPostgres(
            _ => () => new NpgsqlConnection(db.ConnectionString),
            new ValtuutusPostgresOptions("authz", "transactions", "relation_tuples", "attributes"));
        services.AddScoped<IHouseholdGuard, ValtuutusHouseholdGuard>();
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
    public async Task Owner_is_member_and_can_manage_after_create()
    {
        var ownerId = Guid.NewGuid();

        Guid householdId;
        await using (var s = Scope(ownerId))
            householdId = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().CreateAsync("Guard Test House");

        await using var s2 = Scope(ownerId);
        var guard = s2.ServiceProvider.GetRequiredService<IHouseholdGuard>();
        await Assert.That(await guard.IsMemberAsync(householdId, ownerId)).IsTrue();
        await Assert.That(await guard.CanManageAsync(householdId, ownerId)).IsTrue();
        await Assert.That(await guard.CanManageRolesAsync(householdId, ownerId)).IsTrue();
    }

    [Test]
    public async Task Non_member_is_denied_all_permissions()
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        Guid householdId;
        await using (var s = Scope(ownerId))
            householdId = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().CreateAsync("Guard Test House 2");

        await using var s2 = Scope(strangerId);
        var guard = s2.ServiceProvider.GetRequiredService<IHouseholdGuard>();
        await Assert.That(await guard.IsMemberAsync(householdId, strangerId)).IsFalse();
        await Assert.That(await guard.CanManageAsync(householdId, strangerId)).IsFalse();
        await Assert.That(await guard.CanManageRolesAsync(householdId, strangerId)).IsFalse();
    }

    [Test]
    public async Task Joined_member_can_view_but_not_manage()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        Guid householdId;
        await using (var s = Scope(ownerId))
            householdId = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().CreateAsync("Guard Test House 3");

        string token;
        await using (var s = Scope(ownerId))
            token = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().InviteAsync(householdId);

        await using (var s = Scope(memberId))
            await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync(token);

        await using var s4 = Scope(memberId);
        var guard = s4.ServiceProvider.GetRequiredService<IHouseholdGuard>();
        await Assert.That(await guard.IsMemberAsync(householdId, memberId)).IsTrue();
        await Assert.That(await guard.CanManageAsync(householdId, memberId)).IsFalse();
        await Assert.That(await guard.CanManageRolesAsync(householdId, memberId)).IsFalse();
    }

    [Test]
    public async Task Removed_member_loses_access()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        Guid householdId;
        await using (var s = Scope(ownerId))
            householdId = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().CreateAsync("Guard Test House 4");

        string token;
        await using (var s = Scope(ownerId))
            token = await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().InviteAsync(householdId);

        await using (var s = Scope(memberId))
            await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync(token);

        await using (var s = Scope(ownerId))
            await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().RemoveMemberAsync(householdId, memberId);

        await using var s5 = Scope(memberId);
        var guard = s5.ServiceProvider.GetRequiredService<IHouseholdGuard>();
        await Assert.That(await guard.IsMemberAsync(householdId, memberId)).IsFalse();
    }
}
