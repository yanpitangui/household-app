using Dapper;
using HouseholdApp.Application.Modules.Catalog;
using HouseholdApp.Application.Modules.Recipes;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.IntegrationTests.Modules.Recipes;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class RecipeCommandServiceTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScoped<MutableCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<MutableCurrentUser>());
        services.AddPersistence();
        services.AddFusionCache();
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
    public async Task DeleteRecipeAsync_removes_recipe_when_household_matches()
    {
        var householdId = Guid.NewGuid();
        Guid recipeId;
        await using (var s = Scope(Guid.NewGuid()))
            recipeId = await s.ServiceProvider.GetRequiredService<IRecipeCommands>().CreateRecipeAsync(
                householdId, "ToDelete", null, null, null, null, [], []);

        await using (var s = Scope(Guid.NewGuid()))
            await s.ServiceProvider.GetRequiredService<IRecipeCommands>().DeleteRecipeAsync(householdId, recipeId);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM recipes.recipes WHERE id = @recipeId)", new { recipeId });
        await Assert.That(exists).IsFalse();
    }

    [Test]
    public async Task DeleteRecipeAsync_does_not_remove_recipe_when_household_does_not_match()
    {
        var ownerHousehold = Guid.NewGuid();
        var otherHousehold = Guid.NewGuid();
        Guid recipeId;
        await using (var s = Scope(Guid.NewGuid()))
            recipeId = await s.ServiceProvider.GetRequiredService<IRecipeCommands>().CreateRecipeAsync(
                ownerHousehold, "StillHere", null, null, null, null, [], []);

        await using (var s = Scope(Guid.NewGuid()))
            await s.ServiceProvider.GetRequiredService<IRecipeCommands>().DeleteRecipeAsync(otherHousehold, recipeId);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM recipes.recipes WHERE id = @recipeId)", new { recipeId });
        await Assert.That(exists).IsTrue();
    }
}
