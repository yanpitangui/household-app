using Dapper;
using HouseholdApp.Application.Modules.Catalog;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.IntegrationTests.Modules.Catalog;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class CatalogRepositoryTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddPersistence();
        services.AddCatalogModule();
        return services.BuildServiceProvider();
    }

    private AsyncServiceScope QueryScope() => _provider.CreateAsyncScope();

    private AsyncServiceScope WriteScope() => _provider.CreateAsyncScope();

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    // ------------------------------------------------------------------
    // GetCategoriesAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task GetCategoriesAsync_returns_global_categories_for_language()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var catId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji, sort_order) VALUES (@id, NULL, 'en', 'TestFruitsGetGlobal', '🍎', 1)",
            new { id = catId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .GetCategoriesAsync(Guid.NewGuid(), "en");

        await Assert.That(result.Any(c => c.Id == catId)).IsTrue();
    }

    [Test]
    public async Task GetCategoriesAsync_includes_household_categories()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji, sort_order) VALUES (@id, @householdId, NULL, 'MyHouseholdCat', '🏠', 99)",
            new { id = catId, householdId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .GetCategoriesAsync(householdId, "en");

        await Assert.That(result.Any(c => c.Id == catId)).IsTrue();
    }

    [Test]
    public async Task GetCategoriesAsync_excludes_other_household_categories()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var otherHousehold = Guid.NewGuid();
        var catId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji, sort_order) VALUES (@id, @otherHousehold, NULL, 'TheirCatExclude', '🚫', 99)",
            new { id = catId, otherHousehold });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .GetCategoriesAsync(Guid.NewGuid(), "en");

        await Assert.That(result.Any(c => c.Id == catId)).IsFalse();
    }

    // ------------------------------------------------------------------
    // GetCategoriesByIdsAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task GetCategoriesByIdsAsync_returns_empty_for_empty_input()
    {
        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .GetCategoriesByIdsAsync([]);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetCategoriesByIdsAsync_returns_requested_categories()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji) VALUES (@id, NULL, 'en', 'DairyByIds', '🥛')",
            new { id = id1 });
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji) VALUES (@id, NULL, 'en', 'MeatByIds', '🥩')",
            new { id = id2 });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .GetCategoriesByIdsAsync([id1, id2]);

        await Assert.That(result.ContainsKey(id1)).IsTrue();
        await Assert.That(result.ContainsKey(id2)).IsTrue();
        await Assert.That(result[id1].Name).IsEqualTo("DairyByIds");
        await Assert.That(result[id2].Name).IsEqualTo("MeatByIds");
    }

    // ------------------------------------------------------------------
    // SuggestAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task SuggestAsync_returns_item_matching_prefix()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (@id, NULL, 'en', 'LettucePrefix')",
            new { id = itemId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .SuggestAsync(householdId, "Lettu", "en");

        await Assert.That(result.Any(s => s.Id == itemId)).IsTrue();
    }

    [Test]
    public async Task SuggestAsync_returns_household_items_before_global()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var globalId = Guid.NewGuid();
        var hhItemId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (@id, NULL, 'en', 'TomatoOrder')",
            new { id = globalId });
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (@id, @householdId, NULL, 'TomatoOrder')",
            new { id = hhItemId, householdId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .SuggestAsync(householdId, "TomatoOrd", "en");

        await Assert.That(result.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(result[0].Id).IsEqualTo(hhItemId);
    }

    [Test]
    public async Task SuggestAsync_excludes_items_for_other_language()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var ptItemId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (@id, NULL, 'pt-BR', 'UniquePortugueseOnlyItem')",
            new { id = ptItemId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .SuggestAsync(householdId, "UniquePortugueseOnly", "en");

        await Assert.That(result.Any(s => s.Id == ptItemId)).IsFalse();
    }

    [Test]
    public async Task SuggestAsync_includes_category_info()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji) VALUES (@id, NULL, 'en', 'VegetablesCatInfo', '🥦')",
            new { id = catId });
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name, category_id) VALUES (@id, NULL, 'en', 'BroccoliCatInfo', @catId)",
            new { id = itemId, catId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .SuggestAsync(householdId, "BroccoliCat", "en");

        var s = result.First(x => x.Id == itemId);
        await Assert.That(s.CategoryName).IsEqualTo("VegetablesCatInfo");
        await Assert.That(s.CategoryEmoji).IsEqualTo("🥦");
    }

    [Test]
    public async Task SuggestAsync_finds_item_by_unaccented_query()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (@id, @householdId, NULL, 'Pão')",
            new { id = itemId, householdId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .SuggestAsync(householdId, "Pao", "pt-BR");

        await Assert.That(result.Any(s => s.Id == itemId)).IsTrue();
    }

    [Test]
    public async Task SuggestAsync_finds_accented_item_by_accented_query()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (@id, @householdId, NULL, 'Maçã')",
            new { id = itemId, householdId });

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .SuggestAsync(householdId, "Maca", "pt-BR");

        await Assert.That(result.Any(s => s.Id == itemId)).IsTrue();
    }

    // ------------------------------------------------------------------
    // MatchIngredientsAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task MatchIngredientsAsync_matches_plural_portuguese_via_fts()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (gen_random_uuid(), NULL, 'pt-BR', 'batata') ON CONFLICT DO NOTHING");
        var itemId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM catalog.items WHERE lower(name) = 'batata' AND language = 'pt-BR' AND household_id IS NULL");

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .MatchIngredientsAsync(householdId, ["batatas"]);

        await Assert.That(result.ContainsKey("batatas")).IsTrue();
        await Assert.That(result["batatas"]?.Id).IsEqualTo(itemId);
    }

    [Test]
    public async Task MatchIngredientsAsync_matches_english_ingredient_against_en_catalog()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (gen_random_uuid(), NULL, 'en', 'potato') ON CONFLICT DO NOTHING");
        var itemId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM catalog.items WHERE lower(name) = 'potato' AND language = 'en' AND household_id IS NULL");

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .MatchIngredientsAsync(householdId, ["potatoes"]);

        await Assert.That(result.ContainsKey("potatoes")).IsTrue();
        await Assert.That(result["potatoes"]?.Id).IsEqualTo(itemId);
    }

    [Test]
    public async Task MatchIngredientsAsync_returns_empty_for_unmatched_ingredient()
    {
        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .MatchIngredientsAsync(Guid.NewGuid(), ["xyzzy_no_match_ingredient_abc"]);

        await Assert.That(result.ContainsKey("xyzzy_no_match_ingredient_abc")).IsFalse();
    }

    [Test]
    public async Task MatchIngredientsAsync_handles_batch_of_multiple_ingredients()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (gen_random_uuid(), NULL, 'pt-BR', 'batata') ON CONFLICT DO NOTHING");
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name) VALUES (gen_random_uuid(), NULL, 'pt-BR', 'cenoura') ON CONFLICT DO NOTHING");
        var batataId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM catalog.items WHERE lower(name) = 'batata' AND language = 'pt-BR' AND household_id IS NULL");
        var cenourasId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM catalog.items WHERE lower(name) = 'cenoura' AND language = 'pt-BR' AND household_id IS NULL");

        await using var scope = QueryScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICatalogQueries>()
            .MatchIngredientsAsync(householdId, ["batatas", "cenouras", "xyzzy_no_match"]);

        await Assert.That(result["batatas"]?.Id).IsEqualTo(batataId);
        await Assert.That(result["cenouras"]?.Id).IsEqualTo(cenourasId);
        await Assert.That(result.ContainsKey("xyzzy_no_match")).IsFalse();
    }

    // ------------------------------------------------------------------
    // IncrementPopularityAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task IncrementPopularityAsync_increments_popularity()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var itemId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.items (id, household_id, language, name, popularity) VALUES (@id, NULL, 'en', 'PopularItem', 5)",
            new { id = itemId });

        await using var scope = WriteScope();
        await scope.ServiceProvider.GetRequiredService<ICatalogCommands>()
            .IncrementPopularityAsync(itemId);

        var popularity = await conn.QuerySingleAsync<int>(
            "SELECT popularity FROM catalog.items WHERE id = @itemId", new { itemId });
        await Assert.That(popularity).IsEqualTo(6);
    }

    // ------------------------------------------------------------------
    // UpsertHouseholdItemAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task UpsertHouseholdItemAsync_inserts_new_item()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();

        await using var scope = WriteScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        var id = await scope.ServiceProvider.GetRequiredService<ICatalogCommands>()
            .UpsertHouseholdItemAsync(householdId, "CustomBread", null);
        await uow.CommitAsync();

        var exists = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM catalog.items WHERE id = @id AND household_id = @householdId)",
            new { id, householdId });
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task UpsertHouseholdItemAsync_updates_category_on_conflict()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO catalog.categories (id, household_id, language, name, emoji) VALUES (@id, @householdId, NULL, 'BakeryUpsert', '🍞')",
            new { id = catId, householdId });

        Guid itemId;
        await using (var s1 = WriteScope())
        {
            var uow = s1.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            itemId = await s1.ServiceProvider.GetRequiredService<ICatalogCommands>()
                .UpsertHouseholdItemAsync(householdId, "MyBreadUpsert", null);
            await uow.CommitAsync();
        }

        await using (var s2 = WriteScope())
        {
            var uow = s2.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await s2.ServiceProvider.GetRequiredService<ICatalogCommands>()
                .UpsertHouseholdItemAsync(householdId, "MyBreadUpsert", catId);
            await uow.CommitAsync();
        }

        var categoryId = await conn.QuerySingleAsync<Guid?>(
            "SELECT category_id FROM catalog.items WHERE id = @itemId", new { itemId });
        await Assert.That(categoryId).IsEqualTo(catId);
    }

    // ------------------------------------------------------------------
    // AddHouseholdCategoryAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task AddHouseholdCategoryAsync_inserts_and_returns_id()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var householdId = Guid.NewGuid();

        await using var scope = WriteScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        var id = await scope.ServiceProvider.GetRequiredService<ICatalogCommands>()
            .AddHouseholdCategoryAsync(householdId, "Garden", "🌿");
        await uow.CommitAsync();

        var row = await conn.QuerySingleOrDefaultAsync<(string Name, string Emoji)>(
            "SELECT name, emoji FROM catalog.categories WHERE id = @id", new { id });
        await Assert.That(row.Name).IsEqualTo("Garden");
        await Assert.That(row.Emoji).IsEqualTo("🌿");
    }
}
