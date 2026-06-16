using Dapper;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Lists.Application.Operations;

public sealed class ListQueryService(NpgsqlDataSource db) : IListQueries
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
            SELECT id, name, category, sort_order AS SortOrder, is_completed AS IsCompleted
            FROM lists.items WHERE list_id = @listId ORDER BY sort_order
            """,
            new { listId });

        var list = await multi.ReadSingleOrDefaultAsync<(Guid Id, string Name)?>();
        if (list is null) return null;
        var items = (await multi.ReadAsync<ListItemDto>()).ToList();
        return new ListDetail(list.Value.Id, list.Value.Name, items);
    }
}
