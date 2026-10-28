using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ActivityEntryProjectionIntegrationTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private static ExpenseCommandService BuildSut(IDocumentSession session, Guid actorId) => new(
        session,
        new FakeCurrentUser(actorId),
        new ThrowingRecurringExpenseRepository(),
        new ThrowingRecurringJobScheduler(),
        new ThrowingUnitOfWork(),
        new FakeTimeProvider(), NullLogger<ExpenseCommandService>.Instance);

    [Test]
    public async Task RecordExpenseAsync_creates_one_ActivityEntry_with_Added_kind()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await ExpenseTestData.SeedGroupAsync(Store, householdId, groupId);
        var actorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);

        await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(actorId, 500)], [new AllocationDto(actorId, 500)]);

        await using var qs = Store.QuerySession();
        var entries = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Kind).IsEqualTo(ActivityKind.Added);
        await Assert.That(entries[0].ActorUserId).IsEqualTo(actorId);
    }

    [Test]
    public async Task EditExpenseAsync_creates_exactly_one_Edited_entry_not_two()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await ExpenseTestData.SeedGroupAsync(Store, householdId, groupId);
        var actorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);

        var expenseId = await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(actorId, 500)], [new AllocationDto(actorId, 500)]);
        await sut.EditExpenseAsync(
            householdId, expenseId, "Groceries (corrected)", DateTimeOffset.UtcNow,
            [new FundingSourceDto(actorId, 600)], [new AllocationDto(actorId, 600)]);

        await using var qs = Store.QuerySession();
        var entries = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.Count(e => e.Kind == ActivityKind.Removed)).IsEqualTo(0);
        await Assert.That(entries.Count(e => e.Kind == ActivityKind.Edited)).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteExpenseGroupAsync_creates_a_GroupDeleted_entry_naming_the_group()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = "Trip" });
            await s.SaveChangesAsync();
        }

        await using (var session = Store.LightweightSession())
        {
            var sut = BuildSut(session, actorId);
            await sut.RecordExpenseAsync(
                householdId, groupId, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 500)], [new AllocationDto(actorId, 500)]);
        }
        await using (var session = Store.LightweightSession())
            await BuildSut(session, actorId).DeleteExpenseGroupAsync(householdId, groupId);

        await using var qs = Store.QuerySession();
        var entries = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();

        var deleted = entries.Single(e => e.Kind == ActivityKind.GroupDeleted);
        await Assert.That(deleted.GroupName).IsEqualTo("Trip");
    }
}
