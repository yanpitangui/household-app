using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Recipes.Application.Operations;

internal sealed class CachingRecipeQueryService(IRecipeQueries inner, IFusionCache cache) : IRecipeQueries
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public async Task<IReadOnlyList<RecipeSummary>> ListAsync(Guid householdId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<RecipeSummary>>(
            RecipeCacheKeys.List(householdId),
            token => inner.ListAsync(householdId, token),
            EntryOptions,
            token: ct);

    public async Task<RecipeDetail?> GetAsync(Guid householdId, Guid recipeId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<RecipeDetail?>(
            RecipeCacheKeys.Detail(householdId, recipeId),
            token => inner.GetAsync(householdId, recipeId, token),
            EntryOptions,
            token: ct);
}
