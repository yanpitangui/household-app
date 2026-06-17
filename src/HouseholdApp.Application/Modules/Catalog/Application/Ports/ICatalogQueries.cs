namespace HouseholdApp.Application.Modules.Catalog.Application.Ports;

public sealed record CatalogItemSuggestion(
    Guid Id,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryEmoji,
    string? DefaultUnit);

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Emoji,
    int SortOrder,
    bool IsGlobal);

public interface ICatalogQueries
{
    Task<IReadOnlyList<CatalogItemSuggestion>> SuggestAsync(Guid householdId, string query, string language, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(Guid householdId, string language, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, CategoryDto>> GetCategoriesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
