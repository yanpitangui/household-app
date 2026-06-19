using Dapper;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.IntegrationTests.Infrastructure;

namespace HouseholdApp.IntegrationTests.Modules.Recipes;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class RecipeQueryServiceTests(PostgresFixture db)
{
    private readonly IRecipeQueries _sut = new RecipeQueryService(db.DataSource);

    [Test]
    public async Task ListAsync_returns_recipe_summaries_for_household()
    {
        var householdId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO recipes.recipes (id, household_id, title, servings, created_by) VALUES (@id, @householdId, 'Pasta', 4, @by)",
            new { id = Guid.NewGuid(), householdId, by = Guid.NewGuid() });
        await conn.ExecuteAsync(
            "INSERT INTO recipes.recipes (id, household_id, title, servings, created_by) VALUES (@id, @householdId, 'Salad', 2, @by)",
            new { id = Guid.NewGuid(), householdId, by = Guid.NewGuid() });

        var result = await _sut.ListAsync(householdId);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result.Any(r => r.Title == "Pasta" && r.Servings == 4)).IsTrue();
    }

    [Test]
    public async Task ListAsync_returns_empty_for_unknown_household()
    {
        var result = await _sut.ListAsync(Guid.NewGuid());
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAsync_returns_recipe_with_ingredients_and_instructions()
    {
        var householdId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO recipes.recipes (id, household_id, title, notes, created_by) VALUES (@id, @householdId, 'Bread', 'Easy', @by)",
            new { id = recipeId, householdId, by = Guid.NewGuid() });
        await conn.ExecuteAsync(
            "INSERT INTO recipes.ingredients (id, recipe_id, name, quantity, unit, sort_order) VALUES (@id, @recipeId, 'Flour', '2', 'cups', 0)",
            new { id = Guid.NewGuid(), recipeId });
        await conn.ExecuteAsync(
            "INSERT INTO recipes.instructions (id, recipe_id, step_order, text) VALUES (@id, @recipeId, 1, 'Mix everything')",
            new { id = Guid.NewGuid(), recipeId });

        var detail = await _sut.GetAsync(recipeId);

        await Assert.That(detail).IsNotNull();
        await Assert.That(detail!.Id).IsEqualTo(recipeId);
        await Assert.That(detail.Title).IsEqualTo("Bread");
        await Assert.That(detail.Notes).IsEqualTo("Easy");
        await Assert.That(detail.Ingredients.Count).IsEqualTo(1);
        await Assert.That(detail.Ingredients[0].Name).IsEqualTo("Flour");
        await Assert.That(detail.Instructions.Count).IsEqualTo(1);
        await Assert.That(detail.Instructions[0].Text).IsEqualTo("Mix everything");
    }

    [Test]
    public async Task GetAsync_returns_null_for_unknown_recipe()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());
        await Assert.That(result).IsNull();
    }
}
