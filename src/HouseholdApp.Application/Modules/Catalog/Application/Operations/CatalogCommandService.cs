using HouseholdApp.Application.Modules.Catalog.Application.Ports;

namespace HouseholdApp.Application.Modules.Catalog.Application.Operations;

internal sealed class CatalogCommandService(ICatalogRepository repo) : ICatalogCommands
{
    public Task IncrementPopularityAsync(Guid catalogItemId, CancellationToken ct = default)
        => repo.IncrementPopularityAsync(catalogItemId, ct);

    public Task<Guid> UpsertHouseholdItemAsync(Guid householdId, string name, Guid? categoryId, CancellationToken ct = default)
        => repo.UpsertHouseholdItemAsync(householdId, name, categoryId, ct);

    public Task<Guid> AddHouseholdCategoryAsync(Guid householdId, string name, string emoji, CancellationToken ct = default)
        => repo.AddHouseholdCategoryAsync(householdId, name, emoji, ct);

    public Task UpdateHouseholdCategoryAsync(Guid householdId, Guid categoryId, string name, string emoji, CancellationToken ct = default)
        => repo.UpdateHouseholdCategoryAsync(householdId, categoryId, name, emoji, ct);

    public Task DeleteHouseholdCategoryAsync(Guid householdId, Guid categoryId, CancellationToken ct = default)
        => repo.DeleteHouseholdCategoryAsync(householdId, categoryId, ct);
}
