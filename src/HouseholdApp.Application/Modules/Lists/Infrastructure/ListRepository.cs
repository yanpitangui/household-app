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

        var items = list.Items;

        // One round trip regardless of list size: items go in as parallel arrays and unnest
        // back into rows (an empty list unnests to zero rows, so the item upsert is a no-op).
        await conn.ExecuteAsync(
            """
            INSERT INTO lists.lists (id, household_id, name, created_by, created_at)
            VALUES (@Id, @HouseholdId, @Name, @CreatedBy, @CreatedAt)
            ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;

            INSERT INTO lists.items (id, list_id, name, quantity, unit, catalog_item_id, category_id, added_by, sort_order, is_completed)
            SELECT * FROM unnest(
                @ItemIds::uuid[], @ItemListIds::uuid[], @ItemNames::text[], @ItemQuantities::text[], @ItemUnits::text[],
                @ItemCatalogItemIds::uuid[], @ItemCategoryIds::uuid[], @ItemAddedBy::uuid[], @ItemSortOrders::int[], @ItemIsCompleted::bool[])
            ON CONFLICT (id) DO UPDATE
                SET is_completed = EXCLUDED.is_completed,
                    sort_order   = EXCLUDED.sort_order,
                    category_id  = EXCLUDED.category_id,
                    quantity     = EXCLUDED.quantity,
                    unit         = EXCLUDED.unit;
            """,
            new
            {
                list.Id, list.HouseholdId, list.Name, list.CreatedBy, list.CreatedAt,
                ItemIds = items.Select(i => i.Id).ToArray(),
                ItemListIds = items.Select(i => i.ListId).ToArray(),
                ItemNames = items.Select(i => i.Name).ToArray(),
                ItemQuantities = items.Select(i => i.Quantity).ToArray(),
                ItemUnits = items.Select(i => i.Unit).ToArray(),
                ItemCatalogItemIds = items.Select(i => i.CatalogItemId).ToArray(),
                ItemCategoryIds = items.Select(i => i.CategoryId).ToArray(),
                ItemAddedBy = items.Select(i => i.AddedBy).ToArray(),
                ItemSortOrders = items.Select(i => i.SortOrder).ToArray(),
                ItemIsCompleted = items.Select(i => i.IsCompleted).ToArray(),
            }, tx);
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
            "SELECT id, list_id, name, quantity, unit, catalog_item_id, category_id, added_by, sort_order, is_completed FROM lists.items WHERE list_id = @listId ORDER BY sort_order",
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

    public async Task DeleteItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM lists.items WHERE id = ANY(@itemIds)",
            new { itemIds = itemIds.ToArray() },
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
