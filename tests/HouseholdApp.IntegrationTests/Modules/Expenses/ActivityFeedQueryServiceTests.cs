using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.IntegrationTests.Infrastructure;
using Dapper;
using Marten;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ActivityFeedQueryServiceTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private ActivityFeedQueryService BuildSut(IQuerySession qs) =>
        new(qs, new UserRepository(db.DataSource, TimeProvider.System, Substitute.For<IEventBus>()));

    private async Task InsertUserAsync(Guid id, string name)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id, sub = $"sub-{id}", email = $"{id}@test.com", name });
    }

    [Test]
    public async Task GetActivityFeedAsync_resolves_actor_display_name_and_viewer_delta()
    {
        var householdId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await InsertUserAsync(actorId, "Alice");

        await using (var s = Store.LightweightSession())
        {
            s.Store(new ActivityEntry
            {
                Id = Guid.NewGuid(), HouseholdId = householdId, OccurredAt = DateTimeOffset.UtcNow,
                ActorUserId = actorId, Kind = ActivityKind.Added, Description = "Groceries",
                GroupName = "Casa", ViewerDeltaCents = new() { [actorId] = 500 }
            });
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var page = await BuildSut(qs).GetActivityFeedAsync(householdId, actorId, null, 20);

        await Assert.That(page.Items.Count).IsEqualTo(1);
        await Assert.That(page.Items[0].ActorDisplayName).IsEqualTo("Alice");
        await Assert.That(page.Items[0].ViewerDeltaCents).IsEqualTo(500L);
    }

    [Test]
    public async Task GetActivityFeedAsync_returns_null_delta_for_viewer_not_involved()
    {
        var householdId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        await InsertUserAsync(actorId, "Alice");
        await InsertUserAsync(viewerId, "Carol");

        await using (var s = Store.LightweightSession())
        {
            s.Store(new ActivityEntry
            {
                Id = Guid.NewGuid(), HouseholdId = householdId, OccurredAt = DateTimeOffset.UtcNow,
                ActorUserId = actorId, Kind = ActivityKind.Settlement,
                SettlementPayerId = actorId, SettlementRecipientId = Guid.NewGuid(),
                Description = "", ViewerDeltaCents = []
            });
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var page = await BuildSut(qs).GetActivityFeedAsync(householdId, viewerId, null, 20);

        await Assert.That(page.Items[0].ViewerDeltaCents).IsNull();
    }

    [Test]
    public async Task GetActivityFeedAsync_paginates_with_cursor_newest_first()
    {
        var householdId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await InsertUserAsync(actorId, "Alice");

        await using (var s = Store.LightweightSession())
        {
            for (var i = 0; i < 3; i++)
            {
                s.Store(new ActivityEntry
                {
                    Id = Guid.NewGuid(), HouseholdId = householdId,
                    OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                    ActorUserId = actorId, Kind = ActivityKind.Added, Description = $"Item {i}",
                    GroupName = "Casa", ViewerDeltaCents = []
                });
            }
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);

        var firstPage = await sut.GetActivityFeedAsync(householdId, actorId, null, 2);
        await Assert.That(firstPage.Items.Count).IsEqualTo(2);
        await Assert.That(firstPage.Items[0].Description).IsEqualTo("Item 0");
        await Assert.That(firstPage.NextCursor).IsNotNull();

        var secondPage = await sut.GetActivityFeedAsync(householdId, actorId, firstPage.NextCursor, 2);
        await Assert.That(secondPage.Items.Count).IsEqualTo(1);
        await Assert.That(secondPage.Items[0].Description).IsEqualTo("Item 2");
        await Assert.That(secondPage.NextCursor).IsNull();
    }
}
