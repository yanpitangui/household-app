using HouseholdApp.Application.Modules.Households;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Modules.Households.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Households.Infrastructure;

public sealed class HouseholdCacheInvalidationHandlerTests
{
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly HouseholdCacheInvalidationHandler _sut;

    public HouseholdCacheInvalidationHandlerTests()
    {
        _sut = new HouseholdCacheInvalidationHandler(_cache);
    }

    [Test]
    public async Task HouseholdCreated_clears_the_owners_lists()
    {
        var ownerId = Guid.NewGuid();
        await _cache.SetAsync(HouseholdCacheKeys.ListForUser(ownerId), "stale-list");
        await _cache.SetAsync(HouseholdCacheKeys.ListNames(ownerId), "stale-names");

        await _sut.HandleAsync(new HouseholdCreated(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), ownerId));

        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListForUser(ownerId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListNames(ownerId))).HasValue).IsFalse();
    }

    [Test]
    public async Task HouseholdMemberJoined_clears_members_detail_and_the_joining_users_lists()
    {
        var householdId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _cache.SetAsync(HouseholdCacheKeys.Members(householdId), "stale-members");
        await _cache.SetAsync(HouseholdCacheKeys.Detail(householdId), "stale-detail");
        await _cache.SetAsync(HouseholdCacheKeys.ListForUser(userId), "stale-list");
        await _cache.SetAsync(HouseholdCacheKeys.ListNames(userId), "stale-names");

        await _sut.HandleAsync(new HouseholdMemberJoined(Guid.NewGuid(), DateTimeOffset.UtcNow, householdId, userId, HouseholdRole.Member));

        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.Members(householdId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.Detail(householdId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListForUser(userId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListNames(userId))).HasValue).IsFalse();
    }

    [Test]
    public async Task HouseholdMemberRemoved_clears_members_detail_and_the_removed_users_lists()
    {
        var householdId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _cache.SetAsync(HouseholdCacheKeys.Members(householdId), "stale-members");
        await _cache.SetAsync(HouseholdCacheKeys.Detail(householdId), "stale-detail");
        await _cache.SetAsync(HouseholdCacheKeys.ListForUser(userId), "stale-list");
        await _cache.SetAsync(HouseholdCacheKeys.ListNames(userId), "stale-names");

        await _sut.HandleAsync(new HouseholdMemberRemoved(Guid.NewGuid(), DateTimeOffset.UtcNow, householdId, userId));

        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.Members(householdId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.Detail(householdId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListForUser(userId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListNames(userId))).HasValue).IsFalse();
    }

    [Test]
    public async Task HouseholdRoleChanged_clears_members_and_detail_but_not_the_users_lists()
    {
        var householdId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _cache.SetAsync(HouseholdCacheKeys.Members(householdId), "stale-members");
        await _cache.SetAsync(HouseholdCacheKeys.Detail(householdId), "stale-detail");
        await _cache.SetAsync(HouseholdCacheKeys.ListForUser(userId), "unrelated-list");
        await _cache.SetAsync(HouseholdCacheKeys.ListNames(userId), "unrelated-names");

        await _sut.HandleAsync(new HouseholdRoleChanged(Guid.NewGuid(), DateTimeOffset.UtcNow, householdId, userId, HouseholdRole.Admin));

        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.Members(householdId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.Detail(householdId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListForUser(userId))).Value).IsEqualTo("unrelated-list");
        await Assert.That((await _cache.TryGetAsync<string>(HouseholdCacheKeys.ListNames(userId))).Value).IsEqualTo("unrelated-names");
    }
}
