using Dapper;
using HouseholdApp.Application.Modules.Catalog;
using HouseholdApp.Application.Modules.Recipes;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Recipes;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class RecipeQueryServiceTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly IRecipeQueries _sut = new RecipeQueryService(db.DataSource);
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScoped<MutableCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<MutableCurrentUser>());
        services.AddPersistence();
        services.AddCatalogModule();
        services.AddRecipesModule();
        services.AddEventBus();
        return services.BuildServiceProvider();
    }

    private AsyncServiceScope Scope(Guid userId)
    {
        var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().Id = userId;
        return scope;
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

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

    [Test]
    public async Task ProposeListItemsAsync_returns_proposed_items_with_match_for_known_ingredient()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var catalogItemId = Guid.NewGuid();

        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji) VALUES (@id, NULL, 'pt-BR', 'Legumes', '🥬') ON CONFLICT DO NOTHING",
            new { id = catId });
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name, category_id) VALUES (@id, NULL, 'pt-BR', 'batata', @catId) ON CONFLICT DO NOTHING",
            new { id = catalogItemId, catId });

        // Get the actual id for 'batata' in case it already existed (name may be capitalised in seed data)
        catalogItemId = await conn.QuerySingleAsync<Guid>(
            "SELECT id FROM catalog.items WHERE household_id IS NULL AND language = 'pt-BR' AND lower(name) = 'batata'");
        catId = await conn.QuerySingleAsync<Guid>(
            "SELECT category_id FROM catalog.items WHERE id = @catalogItemId", new { catalogItemId });

        Guid recipeId;
        await using (var s = Scope(Guid.NewGuid()))
            recipeId = await s.ServiceProvider.GetRequiredService<IRecipeCommands>().CreateRecipeAsync(
                householdId, "Caldo Verde", null, null, null, null,
                [new IngredientDto("batatas", "4", "médias")],
                []);

        await using var s2 = Scope(Guid.NewGuid());
        var proposed = await s2.ServiceProvider.GetRequiredService<IRecipeListImport>()
            .ProposeListItemsAsync(householdId, recipeId);

        await Assert.That(proposed).HasCount().EqualTo(1);
        var item = proposed[0];
        var expectedName = await conn.QuerySingleAsync<string>(
            "SELECT name FROM catalog.items WHERE id = @catalogItemId", new { catalogItemId });
        await Assert.That(item.Name).IsEqualTo(expectedName); // canonical catalog name, not recipe string
        await Assert.That(item.Quantity).IsEqualTo("4");
        await Assert.That(item.Unit).IsEqualTo("médias");
        await Assert.That(item.Matched).IsTrue();
        await Assert.That(item.CatalogItemId).IsEqualTo(catalogItemId);
    }
}
