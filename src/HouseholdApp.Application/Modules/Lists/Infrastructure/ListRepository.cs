using Dapper;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Domain;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Lists.Infrastructure;

internal sealed record ListRow(Guid Id, Guid HouseholdId, string Name, Guid CreatedBy, DateTimeOffset CreatedAt);

internal sealed class ListRepository(IUnitOfWork uow) : IListRepository
{
    public async Task SaveListAsync(HouseholdList list, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var tx = uow.CurrentTransaction;

        await conn.ExecuteAsync(
            """
            INSERT INTO lists.lists (id, household_id, name, created_by, created_at)
            VALUES (@Id, @HouseholdId, @Name, @CreatedBy, @CreatedAt)
            ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name
            """,
            new { list.Id, list.HouseholdId, list.Name, list.CreatedBy, list.CreatedAt }, tx);

        foreach (var item in list.Items)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO lists.items (id, list_id, name, category, sort_order, is_completed)
                VALUES (@Id, @ListId, @Name, @Category, @SortOrder, @IsCompleted)
                ON CONFLICT (id) DO UPDATE
                    SET is_completed = EXCLUDED.is_completed,
                        sort_order = EXCLUDED.sort_order
                """,
                new { item.Id, item.ListId, item.Name, item.Category, item.SortOrder, item.IsCompleted }, tx);
        }
    }

    public async Task<HouseholdList?> GetAsync(Guid listId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var tx = uow.CurrentTransaction;

        var row = await conn.QuerySingleOrDefaultAsync<ListRow>(
            "SELECT id, household_id, name, created_by, created_at FROM lists.lists WHERE id = @listId",
            new { listId },
            tx);

        if (row is null) return null;

        var items = await conn.QueryAsync<ListItem>(
            "SELECT id, list_id, name, category, sort_order, is_completed FROM lists.items WHERE list_id = @listId ORDER BY sort_order",
            new { listId },
            tx);

        return HouseholdList.Reconstitute(row.Id, row.HouseholdId, row.Name,
            row.CreatedBy, row.CreatedAt, items.ToList());
    }

    public async Task DeleteItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM lists.items WHERE id = @itemId",
            new { itemId },
            uow.CurrentTransaction);
    }

    public async Task DeleteListAsync(Guid listId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM lists.lists WHERE id = @listId",
            new { listId },
            uow.CurrentTransaction);
    }
}
