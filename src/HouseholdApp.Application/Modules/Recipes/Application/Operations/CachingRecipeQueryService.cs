using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Recipes.Application.Operations;

internal sealed class CachingRecipeQueryService(IRecipeQueries inner, IFusionCache cache, TimeProvider time)
    : IRecipeQueries, IRecipeQueriesWithLastModified
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public async Task<IReadOnlyList<RecipeSummary>> ListAsync(Guid householdId, CancellationToken ct = default) =>
        (await GetOrSetListAsync(householdId, ct)).Value;

    public Task<WithLastModified<IReadOnlyList<RecipeSummary>>> ListWithLastModifiedAsync(Guid householdId, CancellationToken ct = default) =>
        GetOrSetListAsync(householdId, ct);

    public async Task<RecipeDetail?> GetAsync(Guid householdId, Guid recipeId, CancellationToken ct = default) =>
        (await GetOrSetDetailAsync(householdId, recipeId, ct)).Value;

    public Task<WithLastModified<RecipeDetail?>> GetWithLastModifiedAsync(Guid householdId, Guid recipeId, CancellationToken ct = default) =>
        GetOrSetDetailAsync(householdId, recipeId, ct);

    private Task<WithLastModified<IReadOnlyList<RecipeSummary>>> GetOrSetListAsync(Guid householdId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithLastModified<IReadOnlyList<RecipeSummary>>>(
            RecipeCacheKeys.List(householdId),
            async token => new WithLastModified<IReadOnlyList<RecipeSummary>>(
                await inner.ListAsync(householdId, token),
                time.GetUtcNow()),
            EntryOptions,
            token: ct).AsTask();

    private Task<WithLastModified<RecipeDetail?>> GetOrSetDetailAsync(Guid householdId, Guid recipeId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithLastModified<RecipeDetail?>>(
            RecipeCacheKeys.Detail(householdId, recipeId),
            async token => new WithLastModified<RecipeDetail?>(
                await inner.GetAsync(householdId, recipeId, token),
                time.GetUtcNow()),
            EntryOptions,
            token: ct).AsTask();
}
