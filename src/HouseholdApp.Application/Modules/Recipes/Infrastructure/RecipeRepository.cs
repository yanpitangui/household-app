using Dapper;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Domain;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Recipes.Infrastructure;

internal sealed class RecipeRepository(IUnitOfWork uow) : IRecipeRepository
{
    public async Task SaveAsync(Recipe recipe, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var tx = uow.CurrentTransaction;

        await conn.ExecuteAsync(
            """
            INSERT INTO recipes.recipes (id, household_id, title, description, servings, source_url, notes, created_by, created_at)
            VALUES (@Id, @HouseholdId, @Title, @Description, @Servings, @SourceUrl, @Notes, @CreatedBy, @CreatedAt)
            ON CONFLICT (id) DO UPDATE
                SET title = EXCLUDED.title,
                    description = EXCLUDED.description,
                    servings = EXCLUDED.servings,
                    notes = EXCLUDED.notes
            """,
            new { recipe.Id, recipe.HouseholdId, recipe.Title, recipe.Description, recipe.Servings, recipe.SourceUrl, recipe.Notes, recipe.CreatedBy, recipe.CreatedAt },
            tx);

        await conn.ExecuteAsync("DELETE FROM recipes.ingredients WHERE recipe_id = @id", new { id = recipe.Id }, tx);
        await conn.ExecuteAsync("DELETE FROM recipes.instructions WHERE recipe_id = @id", new { id = recipe.Id }, tx);

        for (var i = 0; i < recipe.Ingredients.Count; i++)
        {
            var ing = recipe.Ingredients[i];
            await conn.ExecuteAsync(
                "INSERT INTO recipes.ingredients (recipe_id, name, quantity, unit, sort_order) VALUES (@RecipeId, @Name, @Quantity, @Unit, @SortOrder)",
                new { RecipeId = recipe.Id, ing.Name, ing.Quantity, ing.Unit, SortOrder = i },
                tx);
        }

        for (var i = 0; i < recipe.Instructions.Count; i++)
        {
            var step = recipe.Instructions[i];
            await conn.ExecuteAsync(
                "INSERT INTO recipes.instructions (recipe_id, step_order, text) VALUES (@RecipeId, @StepOrder, @Text)",
                new { RecipeId = recipe.Id, StepOrder = step.Order, step.Text },
                tx);
        }
    }

    public async Task DeleteAsync(Guid recipeId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM recipes.recipes WHERE id = @recipeId",
            new { recipeId },
            uow.CurrentTransaction);
    }
}
