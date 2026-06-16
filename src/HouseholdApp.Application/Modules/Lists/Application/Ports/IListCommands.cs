namespace HouseholdApp.Application.Modules.Lists.Application.Ports;

public interface IListCommands
{
    Task<Guid> CreateListAsync(Guid householdId, string name, CancellationToken ct = default);
    Task<Guid> AddItemAsync(Guid listId, string name, string? category, CancellationToken ct = default);
    Task CompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default);
    Task UncompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default);
    Task RemoveItemAsync(Guid listId, Guid itemId, CancellationToken ct = default);
    Task DeleteListAsync(Guid listId, CancellationToken ct = default);
}
