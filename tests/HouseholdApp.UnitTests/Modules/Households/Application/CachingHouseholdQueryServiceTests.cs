using HouseholdApp.Application.Modules.Households;
using HouseholdApp.Application.Modules.Households.Application.Operations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Households.Application;

public sealed class CachingHouseholdQueryServiceTests
{
    private readonly IHouseholdQueries _inner = Substitute.For<IHouseholdQueries>();
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly CachingHouseholdQueryService _sut;

    public CachingHouseholdQueryServiceTests()
    {
        _sut = new CachingHouseholdQueryService(_inner, _cache, _time);
    }

    [Test]
    public async Task GetAsync_caches_result_so_inner_is_called_once()
    {
        var householdId = Guid.NewGuid();
        var detail = new HouseholdDetail(householdId, "Casa", DateTime.UtcNow, []);
        _inner.GetAsync(householdId, Arg.Any<CancellationToken>()).Returns(detail);

        var first = await _sut.GetAsync(householdId);
        var second = await _sut.GetAsync(householdId);

        await Assert.That(first).IsEqualTo(detail);
        await Assert.That(second).IsEqualTo(detail);
        await _inner.Received(1).GetAsync(householdId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ListForUserAsync_caches_result_so_inner_is_called_once()
    {
        var userId = Guid.NewGuid();
        IReadOnlyList<HouseholdSummary> summaries = [new HouseholdSummary(Guid.NewGuid(), "Casa", 2, DateTime.UtcNow)];
        _inner.ListForUserAsync(userId, Arg.Any<CancellationToken>()).Returns(summaries);

        await _sut.ListForUserAsync(userId);
        await _sut.ListForUserAsync(userId);

        await _inner.Received(1).ListForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ListNamesAsync_caches_result_so_inner_is_called_once()
    {
        var userId = Guid.NewGuid();
        IReadOnlyList<HouseholdName> names = [new HouseholdName(Guid.NewGuid(), "Casa")];
        _inner.ListNamesAsync(userId, Arg.Any<CancellationToken>()).Returns(names);

        await _sut.ListNamesAsync(userId);
        await _sut.ListNamesAsync(userId);

        await _inner.Received(1).ListNamesAsync(userId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetWithLastModifiedAsync_returns_same_timestamp_until_cache_invalidated()
    {
        var householdId = Guid.NewGuid();
        var detail = new HouseholdDetail(householdId, "Casa", DateTime.UtcNow, []);
        _inner.GetAsync(householdId, Arg.Any<CancellationToken>()).Returns(detail);

        var first = await _sut.GetWithLastModifiedAsync(householdId);
        var second = await _sut.GetWithLastModifiedAsync(householdId);

        await Assert.That(second.LastModified).IsEqualTo(first.LastModified);
        await Assert.That(second.Value).IsEqualTo(first.Value);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _cache.RemoveAsync(HouseholdCacheKeys.Detail(householdId));
        var third = await _sut.GetWithLastModifiedAsync(householdId);

        await Assert.That(third.LastModified).IsNotEqualTo(first.LastModified);
    }

    [Test]
    public async Task ListForUserWithLastModifiedAsync_returns_same_timestamp_until_cache_invalidated()
    {
        var userId = Guid.NewGuid();
        IReadOnlyList<HouseholdSummary> summaries = [new HouseholdSummary(Guid.NewGuid(), "Casa", 2, DateTime.UtcNow)];
        _inner.ListForUserAsync(userId, Arg.Any<CancellationToken>()).Returns(summaries);

        var first = await _sut.ListForUserWithLastModifiedAsync(userId);
        var second = await _sut.ListForUserWithLastModifiedAsync(userId);

        await Assert.That(second.LastModified).IsEqualTo(first.LastModified);
        await Assert.That(second.Value).IsEqualTo(first.Value);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _cache.RemoveAsync(HouseholdCacheKeys.ListForUser(userId));
        var third = await _sut.ListForUserWithLastModifiedAsync(userId);

        await Assert.That(third.LastModified).IsNotEqualTo(first.LastModified);
    }
}
