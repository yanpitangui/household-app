using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Recipes.Application.Operations;

internal sealed class CachingRecipeQueryService(IRecipeQueries inner, IFusionCache cache)
    : IRecipeQueries, IRecipeQueriesWithETag
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public async Task<IReadOnlyList<RecipeSummary>> ListAsync(Guid householdId, CancellationToken ct = default) =>
        (await GetOrSetListAsync(householdId, ct)).Value;

    public Task<WithETag<IReadOnlyList<RecipeSummary>>> ListWithETagAsync(Guid householdId, CancellationToken ct = default) =>
        GetOrSetListAsync(householdId, ct);

    public async Task<RecipeDetail?> GetAsync(Guid householdId, Guid recipeId, CancellationToken ct = default) =>
        (await GetOrSetDetailAsync(householdId, recipeId, ct)).Value;

    public Task<WithETag<RecipeDetail?>> GetWithETagAsync(Guid householdId, Guid recipeId, CancellationToken ct = default) =>
        GetOrSetDetailAsync(householdId, recipeId, ct);

    private Task<WithETag<IReadOnlyList<RecipeSummary>>> GetOrSetListAsync(Guid householdId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithETag<IReadOnlyList<RecipeSummary>>>(
            RecipeCacheKeys.List(householdId),
            async token => new WithETag<IReadOnlyList<RecipeSummary>>(
                await inner.ListAsync(householdId, token),
                Guid.CreateVersion7().ToString()),
            EntryOptions,
            token: ct).AsTask();

    private Task<WithETag<RecipeDetail?>> GetOrSetDetailAsync(Guid householdId, Guid recipeId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithETag<RecipeDetail?>>(
            RecipeCacheKeys.Detail(householdId, recipeId),
            async token => new WithETag<RecipeDetail?>(
                await inner.GetAsync(householdId, recipeId, token),
                Guid.CreateVersion7().ToString()),
            EntryOptions,
            token: ct).AsTask();
}
