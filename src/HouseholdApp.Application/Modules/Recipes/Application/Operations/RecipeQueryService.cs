using Dapper;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Recipes.Application.Operations;

internal sealed record RecipeRow(
    Guid Id, string Title, string? Description,
    int? Servings, string? SourceUrl, string? Notes, DateTimeOffset CreatedAt);

public sealed class RecipeQueryService(NpgsqlDataSource db) : IRecipeQueries
{
    public async Task<IReadOnlyList<RecipeSummary>> ListAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecipeSummary>(
            """
            SELECT id, title, description, servings, source_url AS SourceUrl
            FROM recipes.recipes WHERE household_id = @householdId
            ORDER BY title
            """,
            new { householdId });
        return rows.ToList();
    }

    public async Task<RecipeDetail?> GetAsync(Guid recipeId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var multi = await conn.QueryMultipleAsync(
            """
            SELECT id, title, description, servings,
                   source_url AS SourceUrl, notes, created_at AS CreatedAt
            FROM recipes.recipes WHERE id = @recipeId;
            SELECT name, quantity, unit FROM recipes.ingredients WHERE recipe_id = @recipeId ORDER BY sort_order;
            SELECT step_order AS Order, text FROM recipes.instructions WHERE recipe_id = @recipeId ORDER BY step_order
            """,
            new { recipeId });

        var row = await multi.ReadSingleOrDefaultAsync<RecipeRow>();
        if (row is null) return null;
        var ingredients = (await multi.ReadAsync<IngredientDto>()).ToList();
        var instructions = (await multi.ReadAsync<InstructionStepDto>()).ToList();
        return new RecipeDetail(
            row.Id, row.Title, row.Description, row.Servings, row.SourceUrl, row.Notes,
            ingredients, instructions, row.CreatedAt);
    }
}
