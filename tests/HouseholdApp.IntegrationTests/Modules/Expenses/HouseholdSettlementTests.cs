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
public sealed class HouseholdSettlementTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private static ExpenseCommandService BuildSut(IDocumentSession session, Guid actorId) => new(
        session, new FakeCurrentUser(actorId), new FakeTimeProvider());

    private sealed record Scenario(Guid HouseholdId, Guid Trip, Guid Home, Guid Leo, Guid Ana);

    // Trip: Leo owes Ana 100. Home: Ana owes Leo 30.
    private async Task<Scenario> SeedAsync()
    {
        var sc = new Scenario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var s = Store.LightweightSession();
        s.Store(new ExpenseGroupDocument { Id = sc.Trip, HouseholdId = sc.HouseholdId, Name = "Trip" });
        s.Store(new ExpenseGroupDocument { Id = sc.Home, HouseholdId = sc.HouseholdId, Name = "Home" });
        await s.SaveChangesAsync();

        var sut = BuildSut(s, sc.Leo);
        await sut.RecordExpenseAsync(sc.HouseholdId, sc.Trip, "Hotel", DateTimeOffset.UtcNow,
            [new FundingSourceDto(sc.Ana, 200)], [new AllocationDto(sc.Ana, 100), new AllocationDto(sc.Leo, 100)]);
        await sut.RecordExpenseAsync(sc.HouseholdId, sc.Home, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(sc.Leo, 60)], [new AllocationDto(sc.Ana, 30), new AllocationDto(sc.Leo, 30)]);
        return sc;
    }

    [Test]
    public async Task Household_settle_writes_tagged_legs_and_zeroes_every_group()
    {
        var sc = await SeedAsync();

        Guid householdSettlementId;
        await using (var s = Store.LightweightSession())
            householdSettlementId = await BuildSut(s, sc.Leo).RecordHouseholdSettlementAsync(
                sc.HouseholdId, sc.Leo, sc.Ana, 70, DateTimeOffset.UtcNow);

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(sc.HouseholdId);
        await Assert.That(ledger!.DebtsBetween(sc.Leo, sc.Ana)).IsEmpty();

        var legs = await qs.Query<SettlementReadModel>()
            .Where(x => x.HouseholdSettlementId == householdSettlementId).ToListAsync();
        await Assert.That(legs.Count).IsEqualTo(2);
        var trip = legs.Single(l => l.ExpenseGroupId == sc.Trip);
        await Assert.That(trip.Id).IsEqualTo(householdSettlementId);
        await Assert.That((trip.PayerId, trip.RecipientId, trip.Cents, trip.IsOffset)).IsEqualTo((sc.Leo, sc.Ana, 100L, false));
        var home = legs.Single(l => l.ExpenseGroupId == sc.Home);
        await Assert.That((home.PayerId, home.RecipientId, home.Cents, home.IsOffset)).IsEqualTo((sc.Ana, sc.Leo, 30L, true));
    }

    [Test]
    public async Task Lead_leg_carries_the_cash_total_and_groups()
    {
        var sc = await SeedAsync();
        Guid id;
        await using (var s = Store.LightweightSession())
            id = await BuildSut(s, sc.Leo).RecordHouseholdSettlementAsync(sc.HouseholdId, sc.Leo, sc.Ana, 40, DateTimeOffset.UtcNow);

        await using var qs = Store.QuerySession();
        var lead = (await qs.Events.FetchStreamAsync(id)).Select(e => e.Data).OfType<SettlementRecorded>().Single();
        await Assert.That(lead.HouseholdSettlementCents).IsEqualTo(40L);
        await Assert.That(lead.HouseholdSettlementGroupIds).IsEquivalentTo(new[] { sc.Trip, sc.Home });
        await Assert.That(lead.Cents).IsEqualTo(70L);
    }

    [Test]
    public async Task Overpaying_a_household_settle_throws_with_the_reason()
    {
        var sc = await SeedAsync();
        await using var s = Store.LightweightSession();

        var ex = await Assert.That(async () => await BuildSut(s, sc.Leo).RecordHouseholdSettlementAsync(
            sc.HouseholdId, sc.Leo, sc.Ana, 71, DateTimeOffset.UtcNow)).Throws<SettlementSplitException>();
        await Assert.That(ex!.Error).IsEqualTo(SettlementSplitError.ExceedsOwed);
    }

    [Test]
    public async Task Deleted_groups_are_left_out_of_a_household_settle()
    {
        var sc = await SeedAsync();
        await using (var s = Store.LightweightSession())
            await BuildSut(s, sc.Leo).DeleteExpenseGroupAsync(sc.HouseholdId, sc.Home);

        // Without Home, Leo owes 100 in Trip only.
        await using (var s = Store.LightweightSession())
            await BuildSut(s, sc.Leo).RecordHouseholdSettlementAsync(sc.HouseholdId, sc.Leo, sc.Ana, 100, DateTimeOffset.UtcNow);

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(sc.HouseholdId);
        await Assert.That(ledger!.DebtsBetween(sc.Leo, sc.Ana)).IsEquivalentTo(new[] { new GroupDebt(sc.Home, -30) });
    }

    [Test]
    public async Task Household_settle_between_two_members_leaves_the_third_untouched()
    {
        var sc = await SeedAsync();
        var bia = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
            await BuildSut(s, sc.Ana).RecordExpenseAsync(sc.HouseholdId, sc.Trip, "Dinner", DateTimeOffset.UtcNow,
                [new FundingSourceDto(sc.Ana, 900)],
                [new AllocationDto(sc.Ana, 300), new AllocationDto(sc.Leo, 300), new AllocationDto(bia, 300)]);

        // Leo now owes Ana 400 in Trip and is owed 30 in Home.
        await using (var s = Store.LightweightSession())
            await BuildSut(s, sc.Leo).RecordHouseholdSettlementAsync(sc.HouseholdId, sc.Leo, sc.Ana, 370, DateTimeOffset.UtcNow);

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(sc.HouseholdId);
        await Assert.That(ledger!.DebtsBetween(sc.Leo, sc.Ana)).IsEmpty();
        await Assert.That(ledger.DebtsBetween(bia, sc.Ana)).IsEquivalentTo(new[] { new GroupDebt(sc.Trip, 300) });
        await Assert.That(ledger.DebtsBetween(bia, sc.Leo)).IsEmpty();
        await Assert.That(ledger.GroupSettledAt.ContainsKey(sc.Trip)).IsFalse();
        await Assert.That(ledger.HouseholdSettledAt).IsNull();
    }

    [Test]
    public async Task Group_settle_records_one_settlement_in_that_group()
    {
        var sc = await SeedAsync();
        await using (var s = Store.LightweightSession())
            await BuildSut(s, sc.Leo).RecordGroupSettlementAsync(sc.HouseholdId, sc.Trip, sc.Leo, sc.Ana, 150, DateTimeOffset.UtcNow);

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(sc.HouseholdId);
        await Assert.That(ledger!.DebtsBetween(sc.Leo, sc.Ana))
            .IsEquivalentTo(new[] { new GroupDebt(sc.Trip, -50), new GroupDebt(sc.Home, -30) });
    }

    [Test]
    public async Task Group_settle_into_a_deleted_group_is_rejected()
    {
        var sc = await SeedAsync();
        await using (var s = Store.LightweightSession())
            await BuildSut(s, sc.Leo).DeleteExpenseGroupAsync(sc.HouseholdId, sc.Trip);

        await using var session = Store.LightweightSession();
        await Assert.That(async () => await BuildSut(session, sc.Leo).RecordGroupSettlementAsync(
            sc.HouseholdId, sc.Trip, sc.Leo, sc.Ana, 10, DateTimeOffset.UtcNow)).Throws<InvalidOperationException>();
    }
}
