using HouseholdApp.Application.Modules.Identity;
using HouseholdApp.Application.Modules.Identity.Domain;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Identity.Infrastructure;

public sealed class UserCacheInvalidationHandlerTests
{
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly UserCacheInvalidationHandler _sut;

    public UserCacheInvalidationHandlerTests()
    {
        _sut = new UserCacheInvalidationHandler(_cache);
    }

    [Test]
    public async Task UserProvisioned_clears_both_id_and_subject_keys()
    {
        var userId = Guid.NewGuid();
        const string subject = "sub-1";
        await _cache.SetAsync(UserCacheKeys.ById(userId), "stale-by-id");
        await _cache.SetAsync(UserCacheKeys.BySubject(subject), "stale-by-subject");

        await _sut.HandleAsync(new UserProvisioned(Guid.NewGuid(), DateTimeOffset.UtcNow, userId, subject));

        await Assert.That((await _cache.TryGetAsync<string>(UserCacheKeys.ById(userId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(UserCacheKeys.BySubject(subject))).HasValue).IsFalse();
    }
}
