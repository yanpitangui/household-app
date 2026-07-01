namespace HouseholdApp.Application.Modules.Catalog.Application.Ports;

public interface ICatalogCommands
{
    Task IncrementPopularityAsync(Guid catalogItemId, CancellationToken ct = default);
    Task<Guid> UpsertHouseholdItemAsync(Guid householdId, string name, Guid? categoryId, CancellationToken ct = default);
    Task<Guid> AddHouseholdCategoryAsync(Guid householdId, string name, string emoji, CancellationToken ct = default);
    Task UpdateHouseholdCategoryAsync(Guid householdId, Guid categoryId, string name, string emoji, CancellationToken ct = default);
    Task DeleteHouseholdCategoryAsync(Guid householdId, Guid categoryId, CancellationToken ct = default);
}
