using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Catalog.Application.Operations;

internal sealed class CachingCatalogQueryService(ICatalogQueries inner, IFusionCache cache) : ICatalogQueries
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public Task<IReadOnlyList<CatalogItemSuggestion>> SuggestAsync(Guid householdId, string query, string language, CancellationToken ct = default)
        => inner.SuggestAsync(householdId, query, language, ct);

    public Task<IReadOnlyDictionary<string, CatalogItemSuggestion>> MatchIngredientsAsync(
        Guid householdId, IReadOnlyList<string> ingredientNames, CancellationToken ct = default)
        => inner.MatchIngredientsAsync(householdId, ingredientNames, ct);

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(Guid householdId, string language, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<CategoryDto>>(
            CatalogCacheKeys.Categories(householdId, language),
            token => inner.GetCategoriesAsync(householdId, language, token),
            EntryOptions,
            token: ct);

    public async Task<IReadOnlyDictionary<Guid, CategoryDto>> GetCategoriesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();
        var result = new Dictionary<Guid, CategoryDto>();
        var missing = new List<Guid>();

        foreach (var id in idList)
        {
            var cached = await cache.TryGetAsync<CategoryDto>(CatalogCacheKeys.CategoryById(id), token: ct);
            if (cached.HasValue) result[id] = cached.Value;
            else missing.Add(id);
        }

        if (missing.Count > 0)
        {
            var fetched = await inner.GetCategoriesByIdsAsync(missing, ct);
            foreach (var (id, dto) in fetched)
            {
                result[id] = dto;
                await cache.SetAsync(CatalogCacheKeys.CategoryById(id), dto, EntryOptions, token: ct);
            }
        }

        return result;
    }
}
