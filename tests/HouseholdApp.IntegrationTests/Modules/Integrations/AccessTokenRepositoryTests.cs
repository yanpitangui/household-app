using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Infrastructure;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.IntegrationTests.Modules.Integrations;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class AccessTokenRepositoryTests(PostgresFixture db) : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddSingleton(db.DataSource)
        .AddScoped<IAccessTokenRepository, AccessTokenRepository>()
        .BuildServiceProvider();

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private IAccessTokenRepository Repo() => _provider.CreateScope().ServiceProvider.GetRequiredService<IAccessTokenRepository>();

    private static AccessTokenRecord NewToken(Guid userId, Guid householdId) => new(
        Guid.CreateVersion7(), userId, householdId, "Ana's phone", Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray(),
        "abcd", [AccessTokenScopes.ListsAdd], Guid.NewGuid(), null, Now, Now.AddDays(90), null, null);

    [Test]
    public async Task Insert_then_FindByHash_round_trips_every_field()
    {
        var repo = Repo();
        var token = NewToken(Guid.NewGuid(), Guid.NewGuid());

        await repo.InsertAsync(token);
        var found = await repo.FindByHashAsync(token.TokenHash);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Id).IsEqualTo(token.Id);
        await Assert.That(found.Scopes).IsEquivalentTo(token.Scopes);
        await Assert.That(found.DefaultListId).IsEqualTo(token.DefaultListId);
        await Assert.That(found.DefaultGroupId).IsNull();
        await Assert.That(found.ExpiresAt).IsEqualTo(token.ExpiresAt);
        await Assert.That(found.LastUsedAt).IsNull();
    }

    [Test]
    public async Task Revoke_only_works_for_the_owner_and_hides_the_token_from_the_list()
    {
        var repo = Repo();
        Guid user = Guid.NewGuid(), household = Guid.NewGuid();
        var token = NewToken(user, household);
        await repo.InsertAsync(token);

        await Assert.That(await repo.RevokeAsync(token.Id, Guid.NewGuid(), household, Now)).IsFalse();
        await Assert.That(await repo.RevokeAsync(token.Id, user, household, Now)).IsTrue();

        await Assert.That(await repo.ListActiveAsync(user, household)).IsEmpty();
        await Assert.That((await repo.FindByHashAsync(token.TokenHash))!.RevokedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task Touch_writes_at_most_once_per_hour()
    {
        var repo = Repo();
        var token = NewToken(Guid.NewGuid(), Guid.NewGuid());
        await repo.InsertAsync(token);

        await repo.TouchAsync(token.Id, Now);
        await repo.TouchAsync(token.Id, Now.AddMinutes(30));
        await Assert.That((await repo.FindByHashAsync(token.TokenHash))!.LastUsedAt).IsEqualTo(Now);

        await repo.TouchAsync(token.Id, Now.AddMinutes(61));
        await Assert.That((await repo.FindByHashAsync(token.TokenHash))!.LastUsedAt).IsEqualTo(Now.AddMinutes(61));
    }

    [Test]
    public async Task Removing_a_member_revokes_their_tokens_in_that_household_only()
    {
        var repo = Repo();
        Guid user = Guid.NewGuid(), household = Guid.NewGuid(), otherHousehold = Guid.NewGuid();
        var here = NewToken(user, household);
        var elsewhere = NewToken(user, otherHousehold);
        await repo.InsertAsync(here);
        await repo.InsertAsync(elsewhere);

        await new RevokeAccessTokensOnMemberRemoved(repo)
            .HandleAsync(new HouseholdMemberRemoved(Guid.NewGuid(), Now, household, user, []));

        await Assert.That((await repo.FindByHashAsync(here.TokenHash))!.RevokedAt).IsEqualTo(Now);
        await Assert.That((await repo.FindByHashAsync(elsewhere.TokenHash))!.RevokedAt).IsNull();
    }
}
