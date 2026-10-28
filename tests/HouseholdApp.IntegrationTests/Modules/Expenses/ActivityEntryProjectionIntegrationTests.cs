using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
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

    [Test]
    public async Task Renaming_a_group_logs_it_and_shows_the_new_name_on_older_entries()
    {
        var householdId = Guid.NewGuid();
        Guid ana = Guid.NewGuid(), leo = Guid.NewGuid();
        Guid trip, home;
        await using (var s = Store.LightweightSession())
        {
            var sut = BuildSut(s, ana);
            trip = await sut.CreateExpenseGroupAsync(householdId, "Trip", null, "✈️");
            home = await sut.CreateExpenseGroupAsync(householdId, "Home", null);
        }
        await using (var s = Store.LightweightSession())
        {
            var sut = BuildSut(s, ana);
            await sut.RecordExpenseAsync(householdId, trip, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(ana, 200)], [new AllocationDto(ana, 100), new AllocationDto(leo, 100)]);
            await sut.RecordExpenseAsync(householdId, home, "Groceries", DateTimeOffset.UtcNow,
                [new FundingSourceDto(leo, 60)], [new AllocationDto(ana, 30), new AllocationDto(leo, 30)]);
        }
        await using (var s = Store.LightweightSession())
            await BuildSut(s, leo).RecordHouseholdSettlementAsync(householdId, leo, ana, 70, DateTimeOffset.UtcNow);

        await using (var s = Store.LightweightSession())
            await BuildSut(s, ana).UpdateExpenseGroupAsync(householdId, trip, "Japan 2027", null, "🗾");

        await using var qs = Store.QuerySession();
        var entries = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();
        await Assert.That(entries.Single(e => e.Description == "Hotel").GroupName).IsEqualTo("Japan 2027");
        await Assert.That(entries.Single(e => e.Description == "Groceries").GroupName).IsEqualTo("Home");
        var settle = entries.Single(e => e.Kind == ActivityKind.HouseholdSettlement);
        await Assert.That(settle.GroupNames[settle.GroupIds.IndexOf(trip)]).IsEqualTo("Japan 2027");
        await Assert.That(settle.GroupNames[settle.GroupIds.IndexOf(home)]).IsEqualTo("Home");
        var renamed = entries.Single(e => e.Kind == ActivityKind.GroupRenamed);
        await Assert.That((renamed.RenamedFrom, renamed.RenamedTo)).IsEqualTo(("Trip", "Japan 2027"));
    }

    [Test]
    public async Task Rebuilding_the_projection_keeps_renamed_group_names_and_rename_entries()
    {
        var householdId = Guid.NewGuid();
        Guid ana = Guid.NewGuid(), leo = Guid.NewGuid();
        Guid trip, home;
        await using (var s = Store.LightweightSession())
        {
            var sut = BuildSut(s, ana);
            trip = await sut.CreateExpenseGroupAsync(householdId, "Trip", null, "✈️");
            home = await sut.CreateExpenseGroupAsync(householdId, "Home", null);
        }
        await using (var s = Store.LightweightSession())
        {
            var sut = BuildSut(s, ana);
            await sut.RecordExpenseAsync(householdId, trip, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(ana, 200)], [new AllocationDto(ana, 100), new AllocationDto(leo, 100)]);
            await sut.RecordExpenseAsync(householdId, home, "Groceries", DateTimeOffset.UtcNow,
                [new FundingSourceDto(leo, 60)], [new AllocationDto(ana, 30), new AllocationDto(leo, 30)]);
        }
        await using (var s = Store.LightweightSession())
            await BuildSut(s, leo).RecordHouseholdSettlementAsync(householdId, leo, ana, 70, DateTimeOffset.UtcNow);
        await using (var s = Store.LightweightSession())
            await BuildSut(s, ana).UpdateExpenseGroupAsync(householdId, trip, "Japan", null, "🗾");
        await using (var s = Store.LightweightSession())
            await BuildSut(s, ana).UpdateExpenseGroupAsync(householdId, trip, "Japan 2027", null, "🗾");

        using (var daemon = await Store.BuildProjectionDaemonAsync())
            await daemon.RebuildProjectionAsync<ActivityEntryProjection>(CancellationToken.None);

        await using var qs = Store.QuerySession();
        var entries = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();
        await Assert.That(entries.Single(e => e.Description == "Hotel").GroupName).IsEqualTo("Japan 2027");
        await Assert.That(entries.Single(e => e.Description == "Groceries").GroupName).IsEqualTo("Home");
        var settle = entries.Single(e => e.Kind == ActivityKind.HouseholdSettlement);
        await Assert.That(settle.GroupNames.Count).IsEqualTo(settle.GroupIds.Count);
        await Assert.That(settle.GroupNames[settle.GroupIds.IndexOf(trip)]).IsEqualTo("Japan 2027");
        await Assert.That(settle.GroupNames[settle.GroupIds.IndexOf(home)]).IsEqualTo("Home");
        var renames = entries.Where(e => e.Kind == ActivityKind.GroupRenamed).Select(e => (e.RenamedFrom, e.RenamedTo));
        await Assert.That(renames).IsEquivalentTo(new[] { ("Trip", "Japan"), ("Japan", "Japan 2027") });
    }

    [Test]
    public async Task Icon_or_description_changes_add_no_activity()
    {
        var householdId = Guid.NewGuid();
        var ana = Guid.NewGuid();
        Guid trip;
        await using (var s = Store.LightweightSession())
            trip = await BuildSut(s, ana).CreateExpenseGroupAsync(householdId, "Trip", null, "✈️");

        await using (var s = Store.LightweightSession())
            await BuildSut(s, ana).UpdateExpenseGroupAsync(householdId, trip, "Trip", "Summer", "🏖️");

        await using var qs = Store.QuerySession();
        await Assert.That(await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task A_second_rename_keeps_the_first_rename_entry_as_written()
    {
        var householdId = Guid.NewGuid();
        var ana = Guid.NewGuid();
        Guid trip;
        await using (var s = Store.LightweightSession())
            trip = await BuildSut(s, ana).CreateExpenseGroupAsync(householdId, "Trip", null);
        await using (var s = Store.LightweightSession())
            await BuildSut(s, ana).UpdateExpenseGroupAsync(householdId, trip, "Japan", null, GroupIcons.Default);
        await using (var s = Store.LightweightSession())
            await BuildSut(s, ana).UpdateExpenseGroupAsync(householdId, trip, "Japan 2027", null, GroupIcons.Default);

        await using var qs = Store.QuerySession();
        var renames = await qs.Query<ActivityEntry>()
            .Where(a => a.HouseholdId == householdId && a.Kind == ActivityKind.GroupRenamed).ToListAsync();
        await Assert.That(renames.Select(r => (r.RenamedFrom, r.RenamedTo)))
            .IsEquivalentTo(new[] { ("Trip", "Japan"), ("Japan", "Japan 2027") });
    }
}
