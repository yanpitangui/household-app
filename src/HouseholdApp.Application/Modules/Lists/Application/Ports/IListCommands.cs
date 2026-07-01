namespace HouseholdApp.Application.Modules.Lists.Application.Ports;

public sealed record BulkAddItem(string Name, string? Quantity, string? Unit, Guid? CatalogItemId, Guid? CategoryId);

public interface IListCommands
{
    Task<Guid> CreateListAsync(Guid householdId, string name, CancellationToken ct = default);
    Task<Guid> AddItemAsync(Guid listId, string name, Guid? catalogItemId, Guid? categoryId, string? quantity = null, string? unit = null, CancellationToken ct = default);
    Task BulkAddItemsAsync(Guid listId, IReadOnlyList<BulkAddItem> items, CancellationToken ct = default);
    Task CompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default);
    Task UncompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default);
    Task RemoveItemAsync(Guid listId, Guid itemId, CancellationToken ct = default);
    Task ChangeItemCategoryAsync(Guid listId, Guid itemId, Guid? categoryId, CancellationToken ct = default);
    Task RemoveCompletedItemsAsync(Guid listId, CancellationToken ct = default);
    Task DeleteListAsync(Guid listId, CancellationToken ct = default);
}
