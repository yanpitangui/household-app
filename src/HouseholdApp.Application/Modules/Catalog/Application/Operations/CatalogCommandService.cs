using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Catalog.Domain;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Catalog.Application.Operations;

internal sealed class CatalogCommandService(ICatalogRepository repo, IEventBus eventBus, TimeProvider time) : ICatalogCommands
{
    public Task IncrementPopularityAsync(Guid catalogItemId, CancellationToken ct = default)
        => repo.IncrementPopularityAsync(catalogItemId, ct);

    public Task<Guid> UpsertHouseholdItemAsync(Guid householdId, string name, Guid? categoryId, CancellationToken ct = default)
        => repo.UpsertHouseholdItemAsync(householdId, name, categoryId, ct);

    public async Task<Guid> AddHouseholdCategoryAsync(Guid householdId, string name, string emoji, CancellationToken ct = default)
    {
        var categoryId = await repo.AddHouseholdCategoryAsync(householdId, name, emoji, ct);
        await eventBus.PublishAsync(new CategoryAdded(Guid.CreateVersion7(), time.GetUtcNow(), householdId, categoryId), ct);
        return categoryId;
    }

    public async Task UpdateHouseholdCategoryAsync(Guid householdId, Guid categoryId, string name, string emoji, CancellationToken ct = default)
    {
        await repo.UpdateHouseholdCategoryAsync(householdId, categoryId, name, emoji, ct);
        await eventBus.PublishAsync(new CategoryUpdated(Guid.CreateVersion7(), time.GetUtcNow(), householdId, categoryId), ct);
    }

    public async Task DeleteHouseholdCategoryAsync(Guid householdId, Guid categoryId, CancellationToken ct = default)
    {
        await repo.DeleteHouseholdCategoryAsync(householdId, categoryId, ct);
        await eventBus.PublishAsync(new CategoryDeleted(Guid.CreateVersion7(), time.GetUtcNow(), householdId, categoryId), ct);
    }
}
