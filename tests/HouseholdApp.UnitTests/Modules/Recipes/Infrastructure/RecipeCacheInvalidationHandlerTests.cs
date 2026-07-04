using HouseholdApp.Application.Modules.Recipes;
using HouseholdApp.Application.Modules.Recipes.Domain;
using HouseholdApp.Application.Modules.Recipes.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Recipes.Infrastructure;

public sealed class RecipeCacheInvalidationHandlerTests
{
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly RecipeCacheInvalidationHandler _sut;

    public RecipeCacheInvalidationHandlerTests()
    {
        _sut = new RecipeCacheInvalidationHandler(_cache);
    }

    [Test]
    public async Task RecipeCreated_clears_the_household_list_key()
    {
        var householdId = Guid.NewGuid();
        await _cache.SetAsync(RecipeCacheKeys.List(householdId), "stale-list");

        await _sut.HandleAsync(new RecipeCreated(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), householdId, Guid.NewGuid(), null));

        await Assert.That((await _cache.TryGetAsync<string>(RecipeCacheKeys.List(householdId))).HasValue).IsFalse();
    }

    [Test]
    public async Task RecipeDeleted_clears_both_list_and_detail_keys()
    {
        var householdId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        await _cache.SetAsync(RecipeCacheKeys.List(householdId), "stale-list");
        await _cache.SetAsync(RecipeCacheKeys.Detail(recipeId), "stale-detail");

        await _sut.HandleAsync(new RecipeDeleted(Guid.NewGuid(), DateTimeOffset.UtcNow, recipeId, householdId, Guid.NewGuid()));

        await Assert.That((await _cache.TryGetAsync<string>(RecipeCacheKeys.List(householdId))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(RecipeCacheKeys.Detail(recipeId))).HasValue).IsFalse();
    }
}
