using HouseholdApp.Application.Modules.Catalog.Application.Ports;

namespace HouseholdApp.Application.Modules.Catalog.Application.Operations;

internal sealed class CatalogQueryService(ICatalogRepository repo) : ICatalogQueries
{
    public Task<IReadOnlyList<CatalogItemSuggestion>> SuggestAsync(Guid householdId, string query, string language, CancellationToken ct = default)
        => repo.SuggestAsync(householdId, query, language, ct);

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(Guid householdId, string language, CancellationToken ct = default)
        => repo.GetCategoriesAsync(householdId, language, ct);

    public Task<IReadOnlyDictionary<Guid, CategoryDto>> GetCategoriesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        => repo.GetCategoriesByIdsAsync(ids, ct);
}
