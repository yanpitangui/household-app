using Dapper;
using HouseholdApp.Application.Modules.Catalog;
using HouseholdApp.Application.Modules.Lists;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Lists;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerClass)]
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
}
