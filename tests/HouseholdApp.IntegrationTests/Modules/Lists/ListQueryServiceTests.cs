using Dapper;
using HouseholdApp.Application.Modules.Lists.Application.Operations;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.IntegrationTests.Infrastructure;

namespace HouseholdApp.IntegrationTests.Modules.Lists;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerClass)]
public sealed class ListQueryServiceTests(PostgresFixture db)
{
    private readonly IListQueries _sut = new ListQueryService(db.DataSource);

    [Test]
    public async Task ListAsync_returns_summaries_with_item_counts()
    {
        var householdId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var listId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO lists.lists (id, household_id, name, created_by) VALUES (@id, @householdId, 'Weekly', @createdBy)",
            new { id = listId, householdId, createdBy = Guid.NewGuid() });
        await conn.ExecuteAsync(
            "INSERT INTO lists.items (id, list_id, name, sort_order) VALUES (@id, @listId, 'Milk', 1000)",
            new { id = Guid.NewGuid(), listId });
        await conn.ExecuteAsync(
            "INSERT INTO lists.items (id, list_id, name, sort_order, is_completed) VALUES (@id, @listId, 'Eggs', 2000, true)",
            new { id = Guid.NewGuid(), listId });

        var result = await _sut.ListAsync(householdId);

        await Assert.That(result.Count).IsEqualTo(1);
        var summary = result[0];
        await Assert.That(summary.Id).IsEqualTo(listId);
        await Assert.That(summary.Name).IsEqualTo("Weekly");
        await Assert.That(summary.TotalItems).IsEqualTo(2L);
        await Assert.That(summary.CompletedItems).IsEqualTo(1L);
    }

    [Test]
    public async Task ListAsync_returns_empty_for_unknown_household()
    {
        var result = await _sut.ListAsync(Guid.NewGuid());
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAsync_returns_list_with_items_in_sort_order()
    {
        var householdId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var listId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO lists.lists (id, household_id, name, created_by) VALUES (@id, @householdId, 'Fruits', @createdBy)",
            new { id = listId, householdId, createdBy = Guid.NewGuid() });

        var item1Id = Guid.NewGuid();
        var item2Id = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO lists.items (id, list_id, name, sort_order) VALUES (@id, @listId, 'Banana', 2000)",
            new { id = item2Id, listId });
        await conn.ExecuteAsync(
            "INSERT INTO lists.items (id, list_id, name, sort_order) VALUES (@id, @listId, 'Apple', 1000)",
            new { id = item1Id, listId });

        var detail = await _sut.GetAsync(listId);

        await Assert.That(detail).IsNotNull();
        await Assert.That(detail!.Id).IsEqualTo(listId);
        await Assert.That(detail.Items.Count).IsEqualTo(2);
        await Assert.That(detail.Items[0].Name).IsEqualTo("Apple");
        await Assert.That(detail.Items[1].Name).IsEqualTo("Banana");
    }

    [Test]
    public async Task GetAsync_returns_null_for_unknown_list()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());
        await Assert.That(result).IsNull();
    }
}
