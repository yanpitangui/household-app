using Dapper;
using HouseholdApp.Application.Modules.Catalog;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Lists;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Lists;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ListCommandServiceTests(PostgresFixture db) : IAsyncDisposable
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
        services.AddCatalogModule();
        services.AddListsModule();
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
    public async Task UncompleteItemAsync_persists_uncompleted_state()
    {
        var userId = Guid.NewGuid();
        var householdId = Guid.NewGuid();

        Guid listId;
        await using (var s = Scope(userId))
            listId = await s.ServiceProvider.GetRequiredService<IListCommands>().CreateListAsync(householdId, "Shopping");

        Guid itemId;
        await using (var s = Scope(userId))
            itemId = await s.ServiceProvider.GetRequiredService<IListCommands>().AddItemAsync(listId, "Milk", null, null);

        await using (var s = Scope(userId))
            await s.ServiceProvider.GetRequiredService<IListCommands>().CompleteItemAsync(listId, itemId);

        await using (var s = Scope(userId))
            await s.ServiceProvider.GetRequiredService<IListCommands>().UncompleteItemAsync(listId, itemId);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var isCompleted = await conn.QuerySingleAsync<bool>(
            "SELECT is_completed FROM lists.items WHERE id = @itemId",
            new { itemId });

        await Assert.That(isCompleted).IsFalse();
    }

    [Test]
    public async Task AddItemAsync_persists_quantity_and_unit()
    {
        var userId = Guid.NewGuid();
        var householdId = Guid.NewGuid();

        Guid listId;
        await using (var s = Scope(userId))
            listId = await s.ServiceProvider.GetRequiredService<IListCommands>().CreateListAsync(householdId, "Test");

        Guid itemId;
        await using (var s = Scope(userId))
            itemId = await s.ServiceProvider.GetRequiredService<IListCommands>()
                .AddItemAsync(listId, "batatas", null, null, quantity: "4", unit: "médias");

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<(string? Qty, string? Unit)>(
            "SELECT quantity, unit FROM lists.items WHERE id = @itemId", new { itemId });

        await Assert.That(row.Qty).IsEqualTo("4");
        await Assert.That(row.Unit).IsEqualTo("médias");
    }

    [Test]
    public async Task BulkAddItemsAsync_adds_all_items_with_quantity_and_category()
    {
        var userId = Guid.NewGuid();
        var householdId = Guid.NewGuid();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var catId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji) VALUES (@id, @householdId, NULL, 'Legumes', '🥬')",
            new { id = catId, householdId });
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name, category_id) VALUES (gen_random_uuid(), NULL, 'pt-BR', 'tomate', @catId) ON CONFLICT DO NOTHING",
            new { catId });
        var catalogItemId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM catalog.items WHERE lower(name) = 'tomate' AND language = 'pt-BR' AND household_id IS NULL");

        Guid listId;
        await using (var s = Scope(userId))
            listId = await s.ServiceProvider.GetRequiredService<IListCommands>().CreateListAsync(householdId, "Mercado");

        var items = new List<BulkAddItem>
        {
            new("batatas", "4", "médias", catalogItemId, catId),
            new("água", "5", "xícaras", null, null),
        };

        await using (var s = Scope(userId))
            await s.ServiceProvider.GetRequiredService<IListCommands>().BulkAddItemsAsync(listId, items);

        var rows = (await conn.QueryAsync<(string Name, string? Qty, string? Unit, Guid? CatId)>(
            "SELECT name, quantity, unit, category_id FROM lists.items WHERE list_id = @listId ORDER BY sort_order",
            new { listId })).ToList();

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].Name).IsEqualTo("batatas");
        await Assert.That(rows[0].Qty).IsEqualTo("4");
        await Assert.That(rows[0].Unit).IsEqualTo("médias");
        await Assert.That(rows[0].CatId).IsEqualTo(catId);
        await Assert.That(rows[1].Name).IsEqualTo("água");
        await Assert.That(rows[1].CatId).IsNull();
    }

    [Test]
    public async Task ChangeItemCategoryAsync_updates_catalog_so_suggestion_includes_category()
    {
        var userId = Guid.NewGuid();
        var householdId = Guid.NewGuid();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var catId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji) VALUES (@id, @householdId, NULL, 'Padaria', '🍞')",
            new { id = catId, householdId });

        Guid listId;
        await using (var s = Scope(userId))
            listId = await s.ServiceProvider.GetRequiredService<IListCommands>().CreateListAsync(householdId, "Lista");

        Guid itemId;
        await using (var s = Scope(userId))
            itemId = await s.ServiceProvider.GetRequiredService<IListCommands>().AddItemAsync(listId, "Pão Francês", null, null);

        await using (var s = Scope(userId))
            await s.ServiceProvider.GetRequiredService<IListCommands>().ChangeItemCategoryAsync(listId, itemId, catId);

        await using var scope = Scope(userId);
        var suggestions = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .SuggestAsync(householdId, "Pão Franc", "pt-BR");

        var match = suggestions.FirstOrDefault(s => s.CategoryId == catId);
        await Assert.That(match).IsNotNull();
        await Assert.That(match!.CategoryName).IsEqualTo("Padaria");
    }
}
