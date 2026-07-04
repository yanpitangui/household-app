using HouseholdApp.Application.Modules.Catalog;
using HouseholdApp.Application.Modules.Catalog.Domain;
using HouseholdApp.Application.Modules.Catalog.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Catalog.Infrastructure;

public sealed class CatalogCacheInvalidationHandlerTests
{
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly CatalogCacheInvalidationHandler _sut;

    public CatalogCacheInvalidationHandlerTests()
    {
        _sut = new CatalogCacheInvalidationHandler(_cache);
    }

    [Test]
    public async Task CategoryAdded_clears_both_language_keys_and_the_per_id_key()
    {
        var householdId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        await _cache.SetAsync(CatalogCacheKeys.Categories(householdId, "en"), "stale-en");
        await _cache.SetAsync(CatalogCacheKeys.Categories(householdId, "pt-BR"), "stale-pt");
        await _cache.SetAsync(CatalogCacheKeys.CategoryById(categoryId), "stale-category");

        await _sut.HandleAsync(new CategoryAdded(Guid.NewGuid(), DateTimeOffset.UtcNow, householdId, categoryId));

        await Assert.That((await _cache.TryGetAsync<string>(CatalogCacheKeys.Categories(householdId, "en"))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(CatalogCacheKeys.Categories(householdId, "pt-BR"))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(CatalogCacheKeys.CategoryById(categoryId))).HasValue).IsFalse();
    }

    [Test]
    public async Task CategoryUpdated_clears_both_language_keys_and_the_per_id_key()
    {
        var householdId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        await _cache.SetAsync(CatalogCacheKeys.Categories(householdId, "en"), "stale-en");
        await _cache.SetAsync(CatalogCacheKeys.CategoryById(categoryId), "stale-category");

        await _sut.HandleAsync(new CategoryUpdated(Guid.NewGuid(), DateTimeOffset.UtcNow, householdId, categoryId));

        await Assert.That((await _cache.TryGetAsync<string>(CatalogCacheKeys.Categories(householdId, "en"))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(CatalogCacheKeys.CategoryById(categoryId))).HasValue).IsFalse();
    }

    [Test]
    public async Task CategoryDeleted_clears_both_language_keys_and_the_per_id_key()
    {
        var householdId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        await _cache.SetAsync(CatalogCacheKeys.Categories(householdId, "pt-BR"), "stale-pt");
        await _cache.SetAsync(CatalogCacheKeys.CategoryById(categoryId), "stale-category");

        await _sut.HandleAsync(new CategoryDeleted(Guid.NewGuid(), DateTimeOffset.UtcNow, householdId, categoryId));

        await Assert.That((await _cache.TryGetAsync<string>(CatalogCacheKeys.Categories(householdId, "pt-BR"))).HasValue).IsFalse();
        await Assert.That((await _cache.TryGetAsync<string>(CatalogCacheKeys.CategoryById(categoryId))).HasValue).IsFalse();
    }
}
