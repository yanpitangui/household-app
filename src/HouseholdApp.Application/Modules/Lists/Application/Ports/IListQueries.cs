namespace HouseholdApp.Application.Modules.Lists.Application.Ports;

public sealed record ListSummary(Guid Id, string Name, long TotalItems, long CompletedItems);
public sealed record ListItemDto(
    Guid Id, string Name,
    Guid? CatalogItemId, Guid? CategoryId, string? CategoryName, string? CategoryEmoji,
    Guid AddedBy, string AddedByName,
    int SortOrder, bool IsCompleted);
public sealed record ListDetail(Guid Id, string Name, IReadOnlyList<ListItemDto> Items);

public interface IListQueries
{
    Task<IReadOnlyList<ListSummary>> ListAsync(Guid householdId, CancellationToken ct = default);
    Task<ListDetail?> GetAsync(Guid listId, CancellationToken ct = default);
}
