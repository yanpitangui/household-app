using HouseholdApp.Application.Modules.Catalog.Application.Ports;

namespace HouseholdApp.Application.Modules.Catalog.Application.Ports;

internal interface ICatalogRepository
{
    Task<IReadOnlyList<CatalogItemSuggestion>> SuggestAsync(Guid householdId, string query, string language, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(Guid householdId, string language, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, CategoryDto>> GetCategoriesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task IncrementPopularityAsync(Guid catalogItemId, CancellationToken ct = default);
    Task<Guid> UpsertHouseholdItemAsync(Guid householdId, string name, Guid? categoryId, CancellationToken ct = default);
    Task<Guid> AddHouseholdCategoryAsync(Guid householdId, string name, string emoji, CancellationToken ct = default);
}
