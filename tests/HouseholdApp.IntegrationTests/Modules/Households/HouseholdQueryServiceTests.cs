using Dapper;
using HouseholdApp.Application.Modules.Households.Application.Operations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.IntegrationTests.Infrastructure;

namespace HouseholdApp.IntegrationTests.Modules.Households;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerClass)]
public sealed class HouseholdQueryServiceTests(PostgresFixture db)
{
    private readonly IHouseholdQueries _sut = new HouseholdQueryService(db.DataSource);

    [Test]
    public async Task ListForUserAsync_returns_households_user_belongs_to()
    {
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var h1 = Guid.NewGuid();
        var h2 = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO households.households (id, name) VALUES (@id, 'Mine'), (@id2, 'Theirs')",
            new { id = h1, id2 = h2 });
        await conn.ExecuteAsync(
            "INSERT INTO households.members (household_id, user_id, role) VALUES (@h, @u, 0), (@h, @owner, 0), (@h2, @owner, 0)",
            new { h = h1, u = userId, owner = otherId, h2 });

        var result = await _sut.ListForUserAsync(userId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Id).IsEqualTo(h1);
        await Assert.That(result[0].Name).IsEqualTo("Mine");
        await Assert.That(result[0].MemberCount).IsEqualTo(2L);
    }

    [Test]
    public async Task ListForUserAsync_returns_empty_when_user_has_no_households()
    {
        var result = await _sut.ListForUserAsync(Guid.NewGuid());
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAsync_returns_household_detail_with_member_display_names()
    {
        var userId = Guid.NewGuid();
        await using var conn = await db.DataSource.OpenConnectionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, 'a@b.com', 'Alice')",
            new { id = userId, sub = $"sub-{userId}" });

        var householdId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO households.households (id, name) VALUES (@id, 'Test House')",
            new { id = householdId });
        await conn.ExecuteAsync(
            "INSERT INTO households.members (household_id, user_id, role) VALUES (@h, @u, 0)",
            new { h = householdId, u = userId });

        var detail = await _sut.GetAsync(householdId);

        await Assert.That(detail).IsNotNull();
        await Assert.That(detail!.Id).IsEqualTo(householdId);
        await Assert.That(detail.Name).IsEqualTo("Test House");
        await Assert.That(detail.Members.Count).IsEqualTo(1);
        await Assert.That(detail.Members[0].UserId).IsEqualTo(userId);
        await Assert.That(detail.Members[0].DisplayName).IsEqualTo("Alice");
    }

    [Test]
    public async Task GetAsync_returns_null_for_unknown_household()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());
        await Assert.That(result).IsNull();
    }
}
