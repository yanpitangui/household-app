using Dapper;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Catalog.Infrastructure;

internal sealed class CatalogRepository(NpgsqlDataSource db) : ICatalogRepository
{
    public async Task<IReadOnlyList<CatalogItemSuggestion>> SuggestAsync(Guid householdId, string query, string language, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var prefix = query + "%";
        var rows = await conn.QueryAsync<CatalogItemSuggestion>(
            """
            SELECT ci.id, ci.name, cc.id AS CategoryId, cc.name AS CategoryName, cc.emoji AS CategoryEmoji, ci.default_unit AS DefaultUnit
            FROM catalog.items ci
            LEFT JOIN catalog.categories cc ON cc.id = ci.category_id
            WHERE (ci.household_id = @householdId OR (ci.household_id IS NULL AND ci.language = @language))
              AND (f_unaccent(ci.name) ILIKE f_unaccent(@prefix) OR similarity(f_unaccent(ci.name), f_unaccent(@query)) > 0.2)
            ORDER BY
                CASE WHEN ci.household_id = @householdId THEN 0 ELSE 1 END,
                ci.popularity DESC,
                similarity(f_unaccent(ci.name), f_unaccent(@query)) DESC
            LIMIT 8
            """,
            new { householdId, language, prefix, query });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(Guid householdId, string language, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(Guid Id, string Name, string Emoji, int SortOrder, bool IsGlobal)>(
            """
            SELECT id, name, emoji, sort_order, (household_id IS NULL) AS IsGlobal
            FROM catalog.categories
            WHERE (household_id = @householdId OR (household_id IS NULL AND language = @language))
            ORDER BY (household_id IS NULL) ASC, sort_order ASC, name ASC
            """,
            new { householdId, language });
        return rows.Select(r => new CategoryDto(r.Id, r.Name, r.Emoji, r.SortOrder, r.IsGlobal)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, CategoryDto>> GetCategoriesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToArray();
        if (idList.Length == 0) return new Dictionary<Guid, CategoryDto>();
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(Guid Id, string Name, string Emoji, int SortOrder)>(
            "SELECT id, name, emoji, sort_order FROM catalog.categories WHERE id = ANY(@ids)",
            new { ids = idList });
        return rows.ToDictionary(r => r.Id, r => new CategoryDto(r.Id, r.Name, r.Emoji, r.SortOrder, true));
    }

    public async Task IncrementPopularityAsync(Guid catalogItemId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE catalog.items SET popularity = popularity + 1, updated_at = now() WHERE id = @catalogItemId",
            new { catalogItemId });
    }

    public async Task<Guid> UpsertHouseholdItemAsync(Guid householdId, string name, Guid? categoryId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO catalog.items (household_id, language, name, category_id, popularity)
            VALUES (@householdId, NULL, @name, @categoryId, 1)
            ON CONFLICT (household_id, lower(name)) WHERE household_id IS NOT NULL
            DO UPDATE SET category_id = EXCLUDED.category_id,
                         popularity   = catalog.items.popularity + 1,
                         updated_at   = now()
            RETURNING id
            """,
            new { householdId, name, categoryId });
        return id;
    }

    public async Task<Guid> AddHouseholdCategoryAsync(Guid householdId, string name, string emoji, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO catalog.categories (household_id, language, name, emoji, sort_order)
            VALUES (@householdId, NULL, @name, @emoji,
                    COALESCE((SELECT MAX(sort_order) + 1 FROM catalog.categories WHERE household_id = @householdId), 100))
            RETURNING id
            """,
            new { householdId, name, emoji });
        return id;
    }
}
