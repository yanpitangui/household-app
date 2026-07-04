using HouseholdApp.Application.Modules.Identity;
using HouseholdApp.Application.Modules.Identity.Application.Operations;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Identity.Application;

public sealed class CachingUserQueryServiceTests
{
    private readonly IUserQuery _inner = Substitute.For<IUserQuery>();
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly CachingUserQueryService _sut;

    public CachingUserQueryServiceTests()
    {
        _sut = new CachingUserQueryService(_inner, _cache);
    }

    [Test]
    public async Task GetByIdAsync_caches_result_so_inner_is_called_once()
    {
        var userId = Guid.NewGuid();
        var profile = new UserProfile(userId, "sub-1", "a@x.com", "Alice", null);
        _inner.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(profile);

        await _sut.GetByIdAsync(userId);
        await _sut.GetByIdAsync(userId);

        await _inner.Received(1).GetByIdAsync(userId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetBySubjectAsync_caches_result_so_inner_is_called_once()
    {
        var profile = new UserProfile(Guid.NewGuid(), "sub-1", "a@x.com", "Alice", null);
        _inner.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(profile);

        await _sut.GetBySubjectAsync("sub-1");
        await _sut.GetBySubjectAsync("sub-1");

        await _inner.Received(1).GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetByIdsAsync_only_queries_inner_for_cache_misses()
    {
        var cachedId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var cachedProfile = new UserProfile(cachedId, "sub-cached", "cached@x.com", "Cached", null);
        var missingProfile = new UserProfile(missingId, "sub-missing", "missing@x.com", "Missing", null);
        await _cache.SetAsync(UserCacheKeys.ById(cachedId), cachedProfile);
        _inner.GetByIdsAsync(Arg.Is<IEnumerable<Guid>>(ids => ids.Single() == missingId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<UserProfile>)[missingProfile]);

        var result = await _sut.GetByIdsAsync([cachedId, missingId]);

        await Assert.That(result).Contains(cachedProfile);
        await Assert.That(result).Contains(missingProfile);
        await _inner.Received(1).GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
    }
}
