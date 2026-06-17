using Dapper;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Lists.Application.Operations;

public sealed class ListQueryService(NpgsqlDataSource db, ICatalogQueries catalogQueries, IUserQuery userQuery) : IListQueries
{
    public async Task<IReadOnlyList<ListSummary>> ListAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ListSummary>(
            """
            SELECT l.id, l.name,
                   COUNT(i.id) AS TotalItems,
                   COUNT(i.id) FILTER (WHERE i.is_completed) AS CompletedItems
            FROM lists.lists l
            LEFT JOIN lists.items i ON i.list_id = l.id
            WHERE l.household_id = @householdId
            GROUP BY l.id, l.name
            ORDER BY l.created_at DESC
            """,
            new { householdId });
        return rows.ToList();
    }

    public async Task<ListDetail?> GetAsync(Guid listId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var multi = await conn.QueryMultipleAsync(
            """
            SELECT id, name FROM lists.lists WHERE id = @listId;
            SELECT id, name, catalog_item_id AS CatalogItemId, category_id AS CategoryId,
                   added_by AS AddedBy, sort_order AS SortOrder, is_completed AS IsCompleted
            FROM lists.items WHERE list_id = @listId ORDER BY sort_order
            """,
            new { listId });

        var list = await multi.ReadSingleOrDefaultAsync<(Guid Id, string Name)?>();
        if (list is null) return null;

        var rawItems = (await multi.ReadAsync<RawListItem>()).ToList();

        var categoryIds = rawItems
            .Where(i => i.CategoryId.HasValue)
            .Select(i => i.CategoryId!.Value)
            .Distinct();
        var categories = await catalogQueries.GetCategoriesByIdsAsync(categoryIds, ct);

        var userIds = rawItems
            .Where(i => i.AddedBy != Guid.Empty)
            .Select(i => i.AddedBy)
            .Distinct();
        var users = (await userQuery.GetByIdsAsync(userIds, ct))
            .ToDictionary(u => u.Id);

        var items = rawItems.Select(i =>
        {
            categories.TryGetValue(i.CategoryId ?? Guid.Empty, out var cat);
            users.TryGetValue(i.AddedBy, out var user);
            return new ListItemDto(
                i.Id, i.Name,
                i.CatalogItemId, i.CategoryId, cat?.Name, cat?.Emoji,
                i.AddedBy, user?.DisplayName ?? string.Empty,
                i.SortOrder, i.IsCompleted);
        }).ToList();

        return new ListDetail(list.Value.Id, list.Value.Name, items);
    }

    private sealed record RawListItem(
        Guid Id, string Name,
        Guid? CatalogItemId, Guid? CategoryId,
        Guid AddedBy, int SortOrder, bool IsCompleted);
}
