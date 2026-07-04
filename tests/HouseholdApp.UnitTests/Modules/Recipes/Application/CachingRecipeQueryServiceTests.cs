using HouseholdApp.Application.Modules.Recipes;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Recipes.Application;

public sealed class CachingRecipeQueryServiceTests
{
    private readonly IRecipeQueries _inner = Substitute.For<IRecipeQueries>();
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly CachingRecipeQueryService _sut;

    public CachingRecipeQueryServiceTests()
    {
        _sut = new CachingRecipeQueryService(_inner, _cache);
    }

    [Test]
    public async Task ListAsync_caches_result_so_inner_is_called_once()
    {
        var householdId = Guid.NewGuid();
        IReadOnlyList<RecipeSummary> recipes = [new RecipeSummary(Guid.NewGuid(), "Bread", null, 4, null)];
        _inner.ListAsync(householdId, Arg.Any<CancellationToken>()).Returns(recipes);

        await _sut.ListAsync(householdId);
        await _sut.ListAsync(householdId);

        await _inner.Received(1).ListAsync(householdId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAsync_caches_result_so_inner_is_called_once()
    {
        var recipeId = Guid.NewGuid();
        var detail = new RecipeDetail(recipeId, "Bread", null, 4, null, null, [], [], DateTimeOffset.UtcNow);
        _inner.GetAsync(recipeId, Arg.Any<CancellationToken>()).Returns(detail);

        await _sut.GetAsync(recipeId);
        await _sut.GetAsync(recipeId);

        await _inner.Received(1).GetAsync(recipeId, Arg.Any<CancellationToken>());
    }
}
