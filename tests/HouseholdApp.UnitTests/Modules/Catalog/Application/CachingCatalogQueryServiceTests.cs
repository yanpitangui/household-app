using HouseholdApp.Application.Modules.Catalog;
using HouseholdApp.Application.Modules.Catalog.Application.Operations;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Catalog.Application;

public sealed class CachingCatalogQueryServiceTests
{
    private readonly ICatalogQueries _inner = Substitute.For<ICatalogQueries>();
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly CachingCatalogQueryService _sut;

    public CachingCatalogQueryServiceTests()
    {
        _sut = new CachingCatalogQueryService(_inner, _cache);
    }

    [Test]
    public async Task GetCategoriesAsync_caches_result_so_inner_is_called_once()
    {
        var householdId = Guid.NewGuid();
        IReadOnlyList<CategoryDto> categories = [new CategoryDto(Guid.NewGuid(), "Fruits", "🍎", 1, false)];
        _inner.GetCategoriesAsync(householdId, "en", Arg.Any<CancellationToken>()).Returns(categories);

        await _sut.GetCategoriesAsync(householdId, "en");
        await _sut.GetCategoriesAsync(householdId, "en");

        await _inner.Received(1).GetCategoriesAsync(householdId, "en", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetCategoriesByIdsAsync_only_queries_inner_for_cache_misses()
    {
        var cachedId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var cachedDto = new CategoryDto(cachedId, "Fruits", "🍎", 1, true);
        var missingDto = new CategoryDto(missingId, "Veggies", "🥕", 2, true);
        await _cache.SetAsync(CatalogCacheKeys.CategoryById(cachedId), cachedDto);
        _inner.GetCategoriesByIdsAsync(Arg.Is<IEnumerable<Guid>>(ids => ids.Single() == missingId), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CategoryDto> { [missingId] = missingDto });

        var result = await _sut.GetCategoriesByIdsAsync([cachedId, missingId]);

        await Assert.That(result[cachedId]).IsEqualTo(cachedDto);
        await Assert.That(result[missingId]).IsEqualTo(missingDto);
        await _inner.Received(1).GetCategoriesByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
    }
}
