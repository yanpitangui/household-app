using HouseholdApp.Application.Modules.Recipes;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.UnitTests.Modules.Recipes.Application;

public sealed class CachingRecipeQueryServiceTests
{
    private readonly IRecipeQueries _inner = Substitute.For<IRecipeQueries>();
    private readonly IFusionCache _cache = new FusionCache(new FusionCacheOptions());
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly CachingRecipeQueryService _sut;

    public CachingRecipeQueryServiceTests()
    {
        _sut = new CachingRecipeQueryService(_inner, _cache, _time);
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
        var householdId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        var detail = new RecipeDetail(recipeId, "Bread", null, 4, null, null, [], [], DateTimeOffset.UtcNow);
        _inner.GetAsync(householdId, recipeId, Arg.Any<CancellationToken>()).Returns(detail);

        await _sut.GetAsync(householdId, recipeId);
        await _sut.GetAsync(householdId, recipeId);

        await _inner.Received(1).GetAsync(householdId, recipeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAsync_does_not_share_cache_entry_across_households_for_the_same_recipeId()
    {
        var recipeId = Guid.NewGuid();
        var householdA = Guid.NewGuid();
        var householdB = Guid.NewGuid();
        var detail = new RecipeDetail(recipeId, "Bread", null, 4, null, null, [], [], DateTimeOffset.UtcNow);
        _inner.GetAsync(Arg.Any<Guid>(), recipeId, Arg.Any<CancellationToken>()).Returns(detail);

        await _sut.GetAsync(householdA, recipeId);
        await _sut.GetAsync(householdB, recipeId);

        await _inner.Received(1).GetAsync(householdA, recipeId, Arg.Any<CancellationToken>());
        await _inner.Received(1).GetAsync(householdB, recipeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ListWithLastModifiedAsync_returns_same_timestamp_until_cache_invalidated()
    {
        var householdId = Guid.NewGuid();
        IReadOnlyList<RecipeSummary> recipes = [new RecipeSummary(Guid.NewGuid(), "Bread", null, 4, null)];
        _inner.ListAsync(householdId, Arg.Any<CancellationToken>()).Returns(recipes);

        var first = await _sut.ListWithLastModifiedAsync(householdId);
        var second = await _sut.ListWithLastModifiedAsync(householdId);

        await Assert.That(second.LastModified).IsEqualTo(first.LastModified);
        await Assert.That(second.Value).IsEqualTo(first.Value);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _cache.RemoveAsync(RecipeCacheKeys.List(householdId));
        var third = await _sut.ListWithLastModifiedAsync(householdId);

        await Assert.That(third.LastModified).IsNotEqualTo(first.LastModified);
    }

    [Test]
    public async Task GetWithLastModifiedAsync_returns_same_timestamp_until_cache_invalidated()
    {
        var householdId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        var detail = new RecipeDetail(recipeId, "Bread", null, 4, null, null, [], [], DateTimeOffset.UtcNow);
        _inner.GetAsync(householdId, recipeId, Arg.Any<CancellationToken>()).Returns(detail);

        var first = await _sut.GetWithLastModifiedAsync(householdId, recipeId);
        var second = await _sut.GetWithLastModifiedAsync(householdId, recipeId);

        await Assert.That(second.LastModified).IsEqualTo(first.LastModified);
        await Assert.That(second.Value).IsEqualTo(first.Value);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _cache.RemoveAsync(RecipeCacheKeys.Detail(householdId, recipeId));
        var third = await _sut.GetWithLastModifiedAsync(householdId, recipeId);

        await Assert.That(third.LastModified).IsNotEqualTo(first.LastModified);
    }
}
