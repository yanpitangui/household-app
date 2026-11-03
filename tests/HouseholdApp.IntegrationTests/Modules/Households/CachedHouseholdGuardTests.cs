using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.IntegrationTests.Modules.Households;

// Same guard as production: Valtuutus with its caching decorator. Repeated checks of one
// permission with different request contexts each get their own cached answer (valtuutus/valtuutus#355).
[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class CachedHouseholdGuardTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = HouseholdGuardTests.BuildProvider(db, caching: true);

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Test]
    public async Task Removal_is_answered_per_target_member_first_then_owner()
    {
        var (ana, bia, leo, id) = await SetUpAsync();
        await using var s = Scope(bia);
        var guard = s.ServiceProvider.GetRequiredService<IHouseholdGuard>();
        await Assert.That(await guard.CanRemoveMemberAsync(id, bia, leo, "Member")).IsTrue();
        await Assert.That(await guard.CanRemoveMemberAsync(id, bia, ana, "Owner")).IsFalse();
    }

    [Test]
    public async Task Removal_is_answered_per_target_owner_first_then_member()
    {
        var (ana, bia, leo, id) = await SetUpAsync();
        await using var s = Scope(bia);
        var guard = s.ServiceProvider.GetRequiredService<IHouseholdGuard>();
        await Assert.That(await guard.CanRemoveMemberAsync(id, bia, ana, "Owner")).IsFalse();
        await Assert.That(await guard.CanRemoveMemberAsync(id, bia, leo, "Member")).IsTrue();
    }

    [Test]
    public async Task Role_change_is_answered_per_target_role_in_either_order()
    {
        var (ana, _, _, id) = await SetUpAsync();
        await using var s = Scope(ana);
        var guard = s.ServiceProvider.GetRequiredService<IHouseholdGuard>();
        await Assert.That(await guard.CanChangeRoleAsync(id, ana, "Member")).IsTrue();
        await Assert.That(await guard.CanChangeRoleAsync(id, ana, "Owner")).IsFalse();
        await Assert.That(await guard.CanChangeRoleAsync(id, ana, "Member")).IsTrue();
    }

    // Ana owns, Bia is an admin, Leo is a plain member.
    private async Task<(Guid Ana, Guid Bia, Guid Leo, Guid Id)> SetUpAsync()
    {
        Guid ana = Guid.NewGuid(), bia = Guid.NewGuid(), leo = Guid.NewGuid();
        Guid id; string token;
        await using (var s = Scope(ana))
        {
            var commands = s.ServiceProvider.GetRequiredService<IHouseholdCommands>();
            id = await commands.CreateAsync("Casa", "America/Sao_Paulo");
            token = await commands.InviteAsync(id);
        }
        foreach (var member in new[] { bia, leo })
            await using (var s = Scope(member))
                await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync(token);
        await using (var s = Scope(ana))
            await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().ChangeMemberRoleAsync(id, bia, "Admin");
        return (ana, bia, leo, id);
    }

    private AsyncServiceScope Scope(Guid userId)
    {
        var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().Id = userId;
        return scope;
    }
}
