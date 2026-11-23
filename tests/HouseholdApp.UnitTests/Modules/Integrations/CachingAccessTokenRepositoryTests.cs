using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Infrastructure;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class CachingAccessTokenRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Household = Guid.NewGuid();
    private readonly IAccessTokenRepository _inner = Substitute.For<IAccessTokenRepository>();
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly CachingAccessTokenRepository _sut;

    public CachingAccessTokenRepositoryTests() => _sut = new CachingAccessTokenRepository(_inner, _cache);

    private static AccessTokenRecord NewToken(DateTimeOffset? lastUsed = null) => new(
        Guid.NewGuid(), Ana, Household, "phone", Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray(),
        "abcd", [AccessTokenScopes.ListsAdd], null, null, Now, Now.AddDays(90), lastUsed, null);

    private AccessTokenRecord Known()
    {
        var token = NewToken();
        _inner.FindByHashAsync(Arg.Is<byte[]>(h => h.SequenceEqual(token.TokenHash)), Arg.Any<CancellationToken>()).Returns(token);
        _inner.ListActiveAsync(Ana, Household, Arg.Any<CancellationToken>()).Returns([token]);
        return token;
    }

    [Test]
    public async Task A_second_lookup_is_served_from_the_cache()
    {
        var token = Known();

        await _sut.FindByHashAsync(token.TokenHash);
        var second = await _sut.FindByHashAsync(token.TokenHash);

        await Assert.That(second!.Id).IsEqualTo(token.Id);
        await _inner.Received(1).FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Unknown_hashes_are_never_cached()
    {
        var hash = Guid.NewGuid().ToByteArray();

        await _sut.FindByHashAsync(hash);
        await _sut.FindByHashAsync(hash);

        await _inner.Received(2).FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Revoking_evicts_the_token()
    {
        var token = Known();
        await _sut.FindByHashAsync(token.TokenHash);
        _inner.RevokeAsync(token.Id, Ana, Household, Now, Arg.Any<CancellationToken>()).Returns(true);

        await _sut.RevokeAsync(token.Id, Ana, Household, Now);
        await _sut.FindByHashAsync(token.TokenHash);

        await _inner.Received(2).FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Revoking_all_evicts_every_token_of_the_member()
    {
        var first = NewToken();
        var second = NewToken();
        _inner.FindByHashAsync(Arg.Is<byte[]>(h => h.SequenceEqual(first.TokenHash)), Arg.Any<CancellationToken>()).Returns(first);
        _inner.FindByHashAsync(Arg.Is<byte[]>(h => h.SequenceEqual(second.TokenHash)), Arg.Any<CancellationToken>()).Returns(second);
        _inner.ListActiveAsync(Ana, Household, Arg.Any<CancellationToken>()).Returns([first, second]);
        await _sut.FindByHashAsync(first.TokenHash);
        await _sut.FindByHashAsync(second.TokenHash);

        await _sut.RevokeAllAsync(Ana, Household, Now);
        await _sut.FindByHashAsync(first.TokenHash);
        await _sut.FindByHashAsync(second.TokenHash);

        await _inner.Received(4).FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Touch_reaches_the_database_once_within_the_hour()
    {
        var token = Known();

        await _sut.TouchAsync(token.Id, Now);
        await _sut.TouchAsync(token.Id, Now.AddMinutes(30));

        await _inner.Received(1).TouchAsync(token.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Touch_never_caches_a_record()
    {
        var token = Known();
        _inner.RevokeAsync(token.Id, Ana, Household, Now, Arg.Any<CancellationToken>()).Returns(true);
        await _sut.FindByHashAsync(token.TokenHash);
        await _sut.RevokeAsync(token.Id, Ana, Household, Now);

        await _sut.TouchAsync(token.Id, Now);
        await _sut.FindByHashAsync(token.TokenHash);

        await _inner.Received(2).FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_lookup_that_raced_a_revoke_leaves_nothing_in_the_cache()
    {
        var token = NewToken();
        var gate = new TaskCompletionSource<AccessTokenRecord?>();
        _inner.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(_ => gate.Task, _ => Task.FromResult<AccessTokenRecord?>(null));
        _inner.ListActiveAsync(Ana, Household, Arg.Any<CancellationToken>()).Returns([token]);
        _inner.RevokeAsync(token.Id, Ana, Household, Now, Arg.Any<CancellationToken>()).Returns(true);

        var lookup = _sut.FindByHashAsync(token.TokenHash);
        await _sut.RevokeAsync(token.Id, Ana, Household, Now);
        gate.SetResult(token);
        await lookup;
        await _sut.FindByHashAsync(token.TokenHash);

        await _inner.Received(2).FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Revoked_records_are_not_cached()
    {
        var token = NewToken() with { RevokedAt = Now };
        _inner.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(token);

        await _sut.FindByHashAsync(token.TokenHash);
        await _sut.FindByHashAsync(token.TokenHash);

        await _inner.Received(2).FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Records_survive_a_serialized_round_trip_through_the_distributed_cache()
    {
        var l2 = new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions()));
        FusionCache Create() => (FusionCache)new FusionCache(new FusionCacheOptions())
            .SetupDistributedCache(l2, new ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson.FusionCacheSystemTextJsonSerializer());
        var record = NewToken(Now) with { DefaultListId = Guid.NewGuid(), DefaultGroupId = Guid.NewGuid() };
        await Create().SetAsync("k", record);

        var read = (await Create().TryGetAsync<AccessTokenRecord>("k")).Value;

        await Assert.That(read.TokenHash.SequenceEqual(record.TokenHash)).IsTrue();
        await Assert.That(read.Scopes).IsEquivalentTo(record.Scopes);
        await Assert.That(read.DefaultListId).IsEqualTo(record.DefaultListId);
        await Assert.That(read.DefaultGroupId).IsEqualTo(record.DefaultGroupId);
        await Assert.That(read.LastUsedAt).IsEqualTo(Now);
        await Assert.That(read.RevokedAt).IsNull();
        await Assert.That(read.ExpiresAt).IsEqualTo(record.ExpiresAt);
    }
}
