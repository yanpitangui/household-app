using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Domain;
using Dapper;
using TUnit.Core.Interfaces;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.IntegrationTests.Infrastructure;
using HouseholdApp.Application.Shared.Persistence;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseQueryServiceTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private ExpenseQueryService BuildSut(IQuerySession querySession) =>
        new(querySession, new UserRepository(db.DataSource, TimeProvider.System, Substitute.For<IEventBus>()), Substitute.For<IHouseholdQueries>(), TimeProvider.System,
            new FakeHouseholdClock(TimeProvider.System));

    private async Task InsertUsersAsync(params Guid[] ids)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        foreach (var id in ids)
            await conn.ExecuteAsync(
                "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
                new { id, sub = $"sub-{id}", email = $"{id}@test.com", name = $"User {id}" });
    }

    private async Task RecordAsync(Guid householdId, Guid payerId, long cents, params Guid[] splitBetween)
    {
        var expenseId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var share = cents / splitBetween.Length;
        await using var s = Store.LightweightSession();
        s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = "Group" });
        s.Events.Append(expenseId, new ExpenseRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, groupId,
            "Groceries", DateTimeOffset.UtcNow,
            [new FundingSource(payerId, cents)],
            splitBetween.Select(id => new Allocation(id, share)).ToList(),
            payerId));
        await s.SaveChangesAsync();
    }

    [Test]
    public async Task Balances_show_the_payer_that_the_other_member_owes_them()
    {
        var householdId = Guid.NewGuid();
        Guid payerId = Guid.NewGuid(), debtorId = Guid.NewGuid();
        await InsertUsersAsync(payerId, debtorId);
        await RecordAsync(householdId, payerId, 1000, payerId, debtorId);

        await using var qs = Store.QuerySession();
        var balances = await BuildSut(qs).GetBalancesAsync(householdId, payerId);

        var only = balances.Single();
        await Assert.That(only.UserId).IsEqualTo(debtorId);
        await Assert.That(only.Cents).IsEqualTo(500L);
    }

    [Test]
    public async Task Balances_show_the_debtor_that_they_owe_the_payer()
    {
        var householdId = Guid.NewGuid();
        Guid payerId = Guid.NewGuid(), debtorId = Guid.NewGuid();
        await InsertUsersAsync(payerId, debtorId);
        await RecordAsync(householdId, payerId, 1000, payerId, debtorId);

        await using var qs = Store.QuerySession();
        var balances = await BuildSut(qs).GetBalancesAsync(householdId, debtorId);

        var only = balances.Single();
        await Assert.That(only.UserId).IsEqualTo(payerId);
        await Assert.That(only.Cents).IsEqualTo(-500L);
    }

    [Test]
    public async Task Balances_list_only_the_viewers_own_pairs()
    {
        var householdId = Guid.NewGuid();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await InsertUsersAsync(a, b, c);
        await RecordAsync(householdId, a, 900, a, b, c);

        await using var qs = Store.QuerySession();
        var balances = await BuildSut(qs).GetBalancesAsync(householdId, b);

        var only = balances.Single();
        await Assert.That(only.UserId).IsEqualTo(a);
        await Assert.That(only.Cents).IsEqualTo(-300L);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private ExpenseQueryService BuildSutAt(IQuerySession querySession, DateTimeOffset now) =>
        new(querySession, new UserRepository(db.DataSource, TimeProvider.System, Substitute.For<IEventBus>()), Substitute.For<IHouseholdQueries>(),
            new FakeTimeProvider(now), new FakeHouseholdClock(new FakeTimeProvider(now)));

    private async Task SeedGroupAsync(Guid householdId, Guid groupId, string name, bool deleted = false)
    {
        await using var s = Store.LightweightSession();
        s.Store(new ExpenseGroupDocument
        {
            Id = groupId, HouseholdId = householdId, Name = name, DeletedAt = deleted ? Now : null
        });
        await s.SaveChangesAsync();
    }

    // Each template is created at createdAt and then has its lifecycle ops applied, all on one stream.
    private async Task<Guid> StartTemplateAsync(
        Guid householdId, Guid groupId, string description, DateOnly firstDate,
        DateTimeOffset? createdAt = null, bool pause = false, bool end = false)
    {
        var at = createdAt ?? Now;
        var userId = Guid.NewGuid();
        var template = RecurringExpense.Create(
            householdId, groupId, description, [new FundingSource(userId, 100_00)], [new Allocation(userId, 100_00)],
            new RecurrenceSchedule(RecurrenceFrequency.Monthly, firstDate, new TimeOnly(9, 0)),
            TimeZoneInfo.Utc, catchUp: false, at, userId);
        if (pause) template.Pause(at, userId);
        if (end) template.End(at, userId);
        await using var s = Store.LightweightSession();
        s.Events.StartStream(template.Id, template.DomainEvents.ToArray());
        await s.SaveChangesAsync();
        return template.Id;
    }

    private async Task<Guid> CreatePlanAsync(
        Guid householdId, Guid groupId, string description, DateOnly firstDate, bool cancel = false)
    {
        var ana = Guid.NewGuid();
        var leo = Guid.NewGuid();
        await InsertUsersAsync(ana, leo);
        await using var session = Store.LightweightSession();
        var sut = new InstallmentPlanCommandService(
            session, new FakeCurrentUser(ana), new FakeTimeProvider(Now), new FakeHouseholdClock(new FakeTimeProvider(Now)), Substitute.For<IEventBus>(),
            Substitute.For<ISessionFactory>(), NullLogger<InstallmentPlanCommandService>.Instance);
        var id = await sut.CreateAsync(householdId, groupId, description, ana, 150000, 0, 5, firstDate, firstDate,
            [new AllocationDto(ana, 75000), new AllocationDto(leo, 75000)]);
        if (cancel) await sut.CancelAsync(householdId, id, voidCreated: false);
        return id;
    }

    [Test]
    public async Task Recurring_page_reports_paused_status()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await SeedGroupAsync(householdId, groupId, "Home");
        var id = await StartTemplateAsync(householdId, groupId, "Rent", new DateOnly(2026, 11, 15), pause: true);

        await using var qs = Store.QuerySession();
        var page = await BuildSutAt(qs, Now).GetRecurringExpensePageAsync(householdId, id, Guid.NewGuid());

        await Assert.That(page!.Status).IsEqualTo(RecurringExpenseStatus.Paused);
    }

    [Test]
    public async Task Group_schedule_counts_count_active_recurring_and_active_plans()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        await SeedGroupAsync(householdId, groupId, "Home");
        await SeedGroupAsync(householdId, otherGroupId, "Trip");
        await StartTemplateAsync(householdId, groupId, "Rent", new DateOnly(2026, 11, 15));
        await StartTemplateAsync(householdId, groupId, "Gym", new DateOnly(2026, 11, 20), pause: true);
        await StartTemplateAsync(
            householdId, groupId, "Streaming", new DateOnly(2026, 10, 20), createdAt: Now.AddDays(-30), end: true);
        await CreatePlanAsync(householdId, groupId, "Sofa", new DateOnly(2026, 11, 12));
        await CreatePlanAsync(householdId, groupId, "Laptop", new DateOnly(2026, 12, 1), cancel: true);
        await StartTemplateAsync(householdId, otherGroupId, "Hotel", new DateOnly(2026, 11, 18));

        await using var qs = Store.QuerySession();
        var counts = await BuildSutAt(qs, Now).GetGroupScheduleCountsAsync(householdId, groupId);

        await Assert.That(counts.ActiveRecurring).IsEqualTo(1);
        await Assert.That(counts.ActivePlans).IsEqualTo(1);
    }

    [Test]
    public async Task Plans_and_recurring_list_orders_active_by_next_date_then_paused_then_ended()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await SeedGroupAsync(householdId, groupId, "Home");
        var activeRecurring = await StartTemplateAsync(householdId, groupId, "Rent", new DateOnly(2026, 11, 15));
        var activePlan = await CreatePlanAsync(householdId, groupId, "Sofa", new DateOnly(2026, 11, 12));
        var paused = await StartTemplateAsync(householdId, groupId, "Gym", new DateOnly(2026, 11, 20), pause: true);
        var cancelledPlan = await CreatePlanAsync(householdId, groupId, "Laptop", new DateOnly(2026, 12, 1), cancel: true);
        var endedRecurring = await StartTemplateAsync(
            householdId, groupId, "Streaming", new DateOnly(2026, 10, 20), createdAt: Now.AddDays(-30), end: true);

        await using var qs = Store.QuerySession();
        var rows = await BuildSutAt(qs, Now).ListPlansAndRecurringAsync(householdId);

        await Assert.That(rows.Select(r => r.Id).ToList()).IsEquivalentTo(
            [activePlan, activeRecurring, paused, cancelledPlan, endedRecurring],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var plan = rows[0];
        await Assert.That(plan.Kind).IsEqualTo(PlansAndRecurringKind.Plan);
        await Assert.That(plan.Bucket).IsEqualTo(PlansAndRecurringBucket.Active);
        await Assert.That(plan.NextDate).IsEqualTo(new DateOnly(2026, 11, 12));
        await Assert.That(plan.AmountCents).IsEqualTo(150000L);
        await Assert.That(plan.Count).IsEqualTo(5);
        await Assert.That(plan.CreatedCount).IsEqualTo(0);
        await Assert.That(plan.PlanStatus).IsEqualTo(InstallmentPlanStatus.Active);
        await Assert.That(plan.GroupName).IsEqualTo("Home");

        var recurring = rows[1];
        await Assert.That(recurring.Kind).IsEqualTo(PlansAndRecurringKind.Recurring);
        await Assert.That(recurring.NextDate).IsEqualTo(new DateOnly(2026, 11, 15));
        await Assert.That(recurring.AmountCents).IsEqualTo(100_00L);
        await Assert.That(recurring.RecurringStatus).IsEqualTo(RecurringExpenseStatus.Active);

        await Assert.That(rows[2].Bucket).IsEqualTo(PlansAndRecurringBucket.Paused);
        await Assert.That(rows[2].NextDate).IsNull();
        await Assert.That(rows[3].Bucket).IsEqualTo(PlansAndRecurringBucket.Ended);
        await Assert.That(rows[3].PlanStatus).IsEqualTo(InstallmentPlanStatus.Cancelled);
        await Assert.That(rows[3].NextDate).IsNull();
        await Assert.That(rows[4].Bucket).IsEqualTo(PlansAndRecurringBucket.Ended);
        await Assert.That(rows[4].RecurringStatus).IsEqualTo(RecurringExpenseStatus.Ended);
    }

    [Test]
    public async Task Plans_and_recurring_list_hides_deleted_groups_and_other_households()
    {
        var householdId = Guid.NewGuid();
        var otherHouseholdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var deletedGroupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        await SeedGroupAsync(householdId, groupId, "Home");
        await SeedGroupAsync(householdId, deletedGroupId, "Old");
        await SeedGroupAsync(otherHouseholdId, otherGroupId, "Theirs");
        var kept = await StartTemplateAsync(householdId, groupId, "Rent", new DateOnly(2026, 11, 15));
        await StartTemplateAsync(householdId, deletedGroupId, "Gym", new DateOnly(2026, 11, 15));
        await CreatePlanAsync(householdId, deletedGroupId, "Sofa", new DateOnly(2026, 11, 12));
        // The group is deleted once its plan exists, since plans are created only in live groups.
        await SeedGroupAsync(householdId, deletedGroupId, "Old", deleted: true);
        await StartTemplateAsync(otherHouseholdId, otherGroupId, "Their rent", new DateOnly(2026, 11, 15));
        await CreatePlanAsync(otherHouseholdId, otherGroupId, "Their sofa", new DateOnly(2026, 11, 12));

        await using var qs = Store.QuerySession();
        var rows = await BuildSutAt(qs, Now).ListPlansAndRecurringAsync(householdId);

        await Assert.That(rows.Select(r => r.Id).ToList()).IsEquivalentTo([kept]);
    }

    [Test]
    public async Task ListExpenseGroupsAsync_returns_groups_for_household_ordered_by_name()
    {
        var householdId = Guid.NewGuid();
        var otherHouseholdId = Guid.NewGuid();

        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = Guid.NewGuid(), HouseholdId = householdId, Name = "Zebra Trip" });
            s.Store(new ExpenseGroupDocument { Id = Guid.NewGuid(), HouseholdId = householdId, Name = "Apple Groceries" });
            s.Store(new ExpenseGroupDocument { Id = Guid.NewGuid(), HouseholdId = otherHouseholdId, Name = "Other Household Group" });
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();

        var groups = await BuildSut(qs).ListExpenseGroupsAsync(householdId);

        await Assert.That(groups[0].Name).IsEqualTo("Apple Groceries");
        await Assert.That(groups[1].Name).IsEqualTo("Zebra Trip");
    }

    private async Task InsertUserAsync(Guid id, string name)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id, sub = $"sub-{id}", email = $"{id}@test.com", name });
    }

    private sealed record TwoGroups(Guid HouseholdId, Guid Trip, Guid Home, Guid Leo, Guid Ana);

    // Trip: Leo owes Ana 100 (Hotel). Home: Ana owes Leo 30 (Groceries).
    private async Task<TwoGroups> SeedTwoGroupsAsync()
    {
        var sc = new TwoGroups(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await InsertUserAsync(sc.Leo, "Leo Test");
        await InsertUserAsync(sc.Ana, "Ana Test");
        await using var s = Store.LightweightSession();
        s.Store(new ExpenseGroupDocument { Id = sc.Trip, HouseholdId = sc.HouseholdId, Name = "Trip" });
        s.Store(new ExpenseGroupDocument { Id = sc.Home, HouseholdId = sc.HouseholdId, Name = "Home" });
        var hotel = Guid.NewGuid();
        var groceries = Guid.NewGuid();
        s.Events.Append(hotel, new ExpenseRecorded(Guid.NewGuid(), DateTimeOffset.UtcNow, hotel, sc.HouseholdId, sc.Trip,
            "Hotel", new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero),
            [new FundingSource(sc.Ana, 200)], [new Allocation(sc.Ana, 100), new Allocation(sc.Leo, 100)], sc.Ana));
        s.Events.Append(groceries, new ExpenseRecorded(Guid.NewGuid(), DateTimeOffset.UtcNow, groceries, sc.HouseholdId, sc.Home,
            "Groceries", new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero),
            [new FundingSource(sc.Leo, 60)], [new Allocation(sc.Ana, 30), new Allocation(sc.Leo, 30)], sc.Leo));
        await s.SaveChangesAsync();
        return sc;
    }

    [Test]
    public async Task Home_sums_groups_into_the_total_and_fills_each_card()
    {
        var sc = await SeedTwoGroupsAsync();

        await using var qs = Store.QuerySession();
        var home = await BuildSut(qs).GetExpensesHomeAsync(sc.HouseholdId, sc.Leo);

        await Assert.That(home.Balances.Single().Cents).IsEqualTo(-70L);
        var trip = home.Groups.Single(g => g.Name == "Trip");
        await Assert.That(trip.Balances.Single().Cents).IsEqualTo(-100L);
        await Assert.That(trip.LastExpenseDescription).IsEqualTo("Hotel");
        await Assert.That(home.Groups.Single(g => g.Name == "Home").Balances.Single().Cents).IsEqualTo(30L);
    }

    [Test]
    public async Task Three_members_each_see_only_their_own_pairs_in_the_total_and_the_cards()
    {
        Guid householdId = Guid.NewGuid(), trip = Guid.NewGuid(), home = Guid.NewGuid();
        Guid leo = Guid.NewGuid(), ana = Guid.NewGuid(), bia = Guid.NewGuid();
        await InsertUserAsync(leo, "Leo Test");
        await InsertUserAsync(ana, "Ana Test");
        await InsertUserAsync(bia, "Bia Test");
        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = trip, HouseholdId = householdId, Name = "Trip" });
            s.Store(new ExpenseGroupDocument { Id = home, HouseholdId = householdId, Name = "Home" });
            // Trip: Ana paid 900 for all three. Home: Leo paid 600 split with Bia.
            var hotel = Guid.NewGuid();
            var rent = Guid.NewGuid();
            s.Events.Append(hotel, new ExpenseRecorded(Guid.NewGuid(), DateTimeOffset.UtcNow, hotel, householdId, trip,
                "Hotel", DateTimeOffset.UtcNow, [new FundingSource(ana, 900)],
                [new Allocation(ana, 300), new Allocation(leo, 300), new Allocation(bia, 300)], ana));
            s.Events.Append(rent, new ExpenseRecorded(Guid.NewGuid(), DateTimeOffset.UtcNow, rent, householdId, home,
                "Rent", DateTimeOffset.UtcNow, [new FundingSource(leo, 600)],
                [new Allocation(leo, 300), new Allocation(bia, 300)], leo));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        static long Of(IEnumerable<MemberBalance> balances, Guid user) => balances.Single(b => b.UserId == user).Cents;

        var forYan = await sut.GetExpensesHomeAsync(householdId, leo);
        await Assert.That(forYan.Balances.Count).IsEqualTo(2);
        await Assert.That(Of(forYan.Balances, ana)).IsEqualTo(-300L);
        await Assert.That(Of(forYan.Balances, bia)).IsEqualTo(300L);
        await Assert.That(forYan.Groups.Single(g => g.Name == "Trip").Balances.Select(b => (b.UserId, b.Cents)))
            .IsEquivalentTo(new[] { (ana, -300L) });
        await Assert.That(forYan.Groups.Single(g => g.Name == "Home").Balances.Select(b => (b.UserId, b.Cents)))
            .IsEquivalentTo(new[] { (bia, 300L) });

        var forBia = await sut.GetExpensesHomeAsync(householdId, bia);
        await Assert.That(Of(forBia.Balances, ana)).IsEqualTo(-300L);
        await Assert.That(Of(forBia.Balances, leo)).IsEqualTo(-300L);

        var forAna = await sut.GetExpensesHomeAsync(householdId, ana);
        await Assert.That(forAna.Groups.Single(g => g.Name == "Trip").Balances.Select(b => (b.UserId, b.Cents)))
            .IsEquivalentTo(new[] { (leo, 300L), (bia, 300L) });
        await Assert.That(forAna.Groups.Single(g => g.Name == "Home").Balances).IsEmpty();
    }

    [Test]
    public async Task Deleted_group_leaves_the_total_and_the_cards()
    {
        var sc = await SeedTwoGroupsAsync();
        await using (var s = Store.LightweightSession())
        {
            var doc = await s.LoadAsync<ExpenseGroupDocument>(sc.Trip);
            doc!.DeletedAt = DateTimeOffset.UtcNow;
            s.Store(doc);
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var home = await sut.GetExpensesHomeAsync(sc.HouseholdId, sc.Leo);

        await Assert.That(home.Balances.Single().Cents).IsEqualTo(30L);
        await Assert.That(home.Groups.Select(g => g.Name)).IsEquivalentTo(new[] { "Home" });
        await Assert.That(await sut.GetGroupPageAsync(sc.HouseholdId, sc.Trip, sc.Leo)).IsNull();
        await Assert.That((await sut.ListExpenseGroupsAsync(sc.HouseholdId)).Select(g => g.Name))
            .IsEquivalentTo(new[] { "Home" });
    }

    [Test]
    public async Task Group_page_lists_its_expenses_payments_and_balance()
    {
        var sc = await SeedTwoGroupsAsync();
        var settlementId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(settlementId, new SettlementRecorded(Guid.NewGuid(), DateTimeOffset.UtcNow, settlementId,
                sc.HouseholdId, sc.Leo, sc.Ana, 40, DateTimeOffset.UtcNow, sc.Leo, sc.Trip,
                HouseholdSettlementId: settlementId));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var page = await BuildSut(qs).GetGroupPageAsync(sc.HouseholdId, sc.Trip, sc.Leo);

        await Assert.That(page!.Name).IsEqualTo("Trip");
        await Assert.That(page.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Hotel" });
        await Assert.That(page.Settlements.Single().IsHouseholdSettle).IsTrue();
        await Assert.That(page.Balances.Single().Cents).IsEqualTo(-60L);
    }

    [Test]
    public async Task Group_page_is_null_for_another_households_group()
    {
        var sc = await SeedTwoGroupsAsync();
        await using var qs = Store.QuerySession();

        await Assert.That(await BuildSut(qs).GetGroupPageAsync(Guid.NewGuid(), sc.Trip, sc.Leo)).IsNull();
    }

    [Test]
    public async Task GetBalancesAsync_can_scope_to_one_group()
    {
        var sc = await SeedTwoGroupsAsync();
        await using var qs = Store.QuerySession();

        var balances = await BuildSut(qs).GetBalancesAsync(sc.HouseholdId, sc.Leo, sc.Home);

        await Assert.That(balances.Single().Cents).IsEqualTo(30L);
    }

    [Test]
    public async Task Preview_names_each_leg_and_reports_errors()
    {
        var sc = await SeedTwoGroupsAsync();
        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);

        var preview = await sut.PreviewHouseholdSettlementAsync(sc.HouseholdId, sc.Leo, sc.Ana, 40);
        await Assert.That(preview.Error).IsNull();
        await Assert.That(preview.OwedCents).IsEqualTo(70L);
        await Assert.That(preview.Legs).IsEquivalentTo(new[]
        {
            new SettlementPreviewLeg(sc.Trip, "Trip", 70, false),
            new SettlementPreviewLeg(sc.Home, "Home", 30, true)
        });

        var over = await sut.PreviewHouseholdSettlementAsync(sc.HouseholdId, sc.Leo, sc.Ana, 100);
        await Assert.That(over.Error).IsEqualTo(SettlementSplitError.ExceedsOwed);
    }

    // ---- Timelines: Recent / Older / keyset paging ----

    private static readonly DateTimeOffset Day0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Pair(Guid HouseholdId, Guid Group, Guid Leo, Guid Ana);

    private async Task<Pair> SeedEmptyGroupAsync(string groupName = "Trip")
    {
        var p = new Pair(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await InsertUserAsync(p.Leo, "Leo Test");
        await InsertUserAsync(p.Ana, "Ana Test");
        await using var s = Store.LightweightSession();
        s.Store(new ExpenseGroupDocument { Id = p.Group, HouseholdId = p.HouseholdId, Name = groupName });
        await s.SaveChangesAsync();
        return p;
    }

    // Leo pays cents and splits it with Ana, so Ana owes Leo cents / 2.
    private async Task<Guid> AddExpenseAsync(
        Pair p, string description, DateTimeOffset date, DateTimeOffset recordedAt, long cents = 100, Guid? group = null, Guid? id = null)
    {
        var expenseId = id ?? Guid.NewGuid();
        await using var s = Store.LightweightSession();
        s.Events.Append(expenseId, new ExpenseRecorded(Guid.NewGuid(), recordedAt, expenseId, p.HouseholdId,
            group ?? p.Group, description, date,
            [new FundingSource(p.Leo, cents)], [new Allocation(p.Leo, cents / 2), new Allocation(p.Ana, cents / 2)], p.Leo));
        await s.SaveChangesAsync();
        return expenseId;
    }

    private async Task<Guid> AddPaymentAsync(
        Pair p, DateTimeOffset date, DateTimeOffset recordedAt, long cents, Guid? group = null)
    {
        var id = Guid.NewGuid();
        await using var s = Store.LightweightSession();
        s.Events.Append(id, new SettlementRecorded(Guid.NewGuid(), recordedAt, id, p.HouseholdId, p.Ana, p.Leo, cents,
            date, p.Ana, group ?? p.Group));
        await s.SaveChangesAsync();
        return id;
    }

    private async Task VoidAsync(Pair p, Guid expenseId, long cents = 100)
    {
        await using var s = Store.LightweightSession();
        s.Events.Append(expenseId, new ExpenseVoided(Guid.NewGuid(), Day0.AddYears(1), expenseId, p.HouseholdId, null,
            [new FundingSource(p.Leo, cents)], [new Allocation(p.Leo, cents / 2), new Allocation(p.Ana, cents / 2)],
            p.Leo, "x", ExpenseGroupId: p.Group));
        await s.SaveChangesAsync();
    }

    [Test]
    public async Task Group_page_starts_at_the_payment_that_settled_up()
    {
        var p = await SeedEmptyGroupAsync();
        await AddExpenseAsync(p, "Dinner", Day0, Day0, 100);
        var payment = await AddPaymentAsync(p, Day0.AddDays(1), Day0.AddDays(1), 50);
        await AddExpenseAsync(p, "Lunch", Day0.AddDays(2), Day0.AddDays(2), 40);

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var page = await sut.GetGroupPageAsync(p.HouseholdId, p.Group, p.Leo);

        await Assert.That(page!.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Lunch" });
        await Assert.That(page.Settlements.Select(x => x.Id)).IsEquivalentTo(new[] { payment });
        await Assert.That(page.HasOlder).IsTrue();

        var older = await sut.GetOlderTimelineAsync(p.HouseholdId, p.Group, null);
        await Assert.That(older.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Dinner" });
        await Assert.That(older.Settlements).IsEmpty();
        await Assert.That(older.NextCursor).IsNull();
        await Assert.That(older.Expenses.Single().RecordedAt).IsEqualTo(Day0);
    }

    [Test]
    public async Task Group_without_a_marker_and_few_entries_has_nothing_older()
    {
        var p = await SeedEmptyGroupAsync();
        await AddExpenseAsync(p, "Dinner", Day0, Day0);

        await using var qs = Store.QuerySession();
        var page = await BuildSut(qs).GetGroupPageAsync(p.HouseholdId, p.Group, p.Leo);

        await Assert.That(page!.Expenses.Count).IsEqualTo(1);
        await Assert.That(page.HasOlder).IsFalse();
    }

    [Test]
    public async Task Backdated_expense_recorded_after_the_settle_is_recent()
    {
        var p = await SeedEmptyGroupAsync();
        await AddExpenseAsync(p, "Dinner", Day0, Day0, 100);
        await AddPaymentAsync(p, Day0.AddDays(1), Day0.AddDays(1), 50);
        await AddExpenseAsync(p, "Forgotten", Day0.AddDays(-30), Day0.AddDays(2), 40);

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var page = await sut.GetGroupPageAsync(p.HouseholdId, p.Group, p.Leo);
        var older = await sut.GetOlderTimelineAsync(p.HouseholdId, p.Group, null);

        await Assert.That(page!.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Forgotten" });
        await Assert.That(older.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Dinner" });
    }

    [Test]
    public async Task Home_without_a_marker_returns_the_latest_30_and_pages_the_rest_without_overlap()
    {
        var p = await SeedEmptyGroupAsync();
        for (var i = 0; i < 38; i++)
            await AddExpenseAsync(p, $"E{i:D2}", Day0.AddDays(i), Day0.AddDays(i), 100);
        for (var i = 0; i < 4; i++)
            await AddPaymentAsync(p, Day0.AddDays(i * 10 + 5), Day0.AddDays(i * 10 + 5), 2);

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var home = await sut.GetExpensesHomeAsync(p.HouseholdId, p.Leo);

        await Assert.That(home.Expenses.Count + home.Settlements.Count).IsEqualTo(30);
        await Assert.That(home.HasOlder).IsTrue();
        await Assert.That(home.Expenses.All(e => e.GroupName == "Trip")).IsTrue();

        var seen = home.Expenses.Select(e => e.Id).Concat(home.Settlements.Select(x => x.Id)).ToList();
        var oldestRecent = home.Expenses.Select(e => e.Date).Concat(home.Settlements.Select(x => x.Date)).Min();
        string? cursor = home.FirstOlderCursor;
        var pages = 0;
        var lastDate = oldestRecent;
        do
        {
            var page = await sut.GetOlderTimelineAsync(p.HouseholdId, null, cursor, pageSize: 5);
            var ids = page.Expenses.Select(e => e.Id).Concat(page.Settlements.Select(x => x.Id)).ToList();
            await Assert.That(ids.Count).IsLessThanOrEqualTo(5);
            var dates = page.Expenses.Select(e => e.Date).Concat(page.Settlements.Select(x => x.Date))
                .OrderByDescending(d => d).ToList();
            await Assert.That(dates.All(d => d <= lastDate)).IsTrue();
            if (dates.Count > 0) lastDate = dates.Min();
            seen.AddRange(ids);
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 20);

        await Assert.That(cursor).IsNull();
        await Assert.That(seen.Count).IsEqualTo(42);
        await Assert.That(seen.Distinct().Count()).IsEqualTo(42);
    }

    [Test]
    public async Task Paging_is_stable_when_an_older_expense_is_voided_between_pages()
    {
        var p = await SeedEmptyGroupAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
            ids.Add(await AddExpenseAsync(p, $"E{i:D2}", Day0.AddDays(i), Day0.AddDays(i), 100));

        await using (var qs = Store.QuerySession())
        {
            var sut = BuildSut(qs);
            var first = await sut.GetOlderTimelineAsync(p.HouseholdId, p.Group, null, pageSize: 4);
            await Assert.That(first.Expenses.Select(e => e.Description))
                .IsEquivalentTo(new[] { "E11", "E10", "E09", "E08" });

            // E08 (already shown) and E05 (still to come) are voided meanwhile.
            await VoidAsync(p, ids[8]);
            await VoidAsync(p, ids[5]);

            await using var qs2 = Store.QuerySession();
            var second = await BuildSut(qs2).GetOlderTimelineAsync(p.HouseholdId, p.Group, first.NextCursor, pageSize: 4);
            await Assert.That(second.Expenses.Select(e => e.Description).ToList())
                .IsEquivalentTo(new[] { "E07", "E06", "E04", "E03" });
            var third = await BuildSut(qs2).GetOlderTimelineAsync(p.HouseholdId, p.Group, second.NextCursor, pageSize: 4);
            await Assert.That(third.Expenses.Select(e => e.Description).ToList())
                .IsEquivalentTo(new[] { "E02", "E01", "E00" });
            await Assert.That(third.NextCursor).IsNull();
        }
    }

    [Test]
    public async Task Rows_sharing_a_date_split_across_pages_keep_id_descending_order()
    {
        var p = await SeedEmptyGroupAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
            ids.Add(await AddExpenseAsync(p, $"Same{i}", Day0, Day0.AddMinutes(i), 100));

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var collected = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await sut.GetOlderTimelineAsync(p.HouseholdId, p.Group, cursor, pageSize: 3);
            collected.AddRange(page.Expenses.Select(e => e.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        await Assert.That(collected.Count).IsEqualTo(7);
        await Assert.That(collected.Distinct().Count()).IsEqualTo(7);
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var sqlOrder = (await conn.QueryAsync<Guid>(
            "SELECT id FROM expenses.mt_doc_expensereadmodel WHERE id = ANY(@ids) ORDER BY id DESC", new { ids = ids.ToArray() })).ToList();
        await Assert.That(collected).IsEquivalentTo(sqlOrder);
        await Assert.That(collected.SequenceEqual(sqlOrder)).IsTrue();
    }

    [Test]
    public async Task Home_lists_active_group_expenses_with_group_names_and_hides_deleted_groups()
    {
        var sc = await SeedTwoGroupsAsync();

        await using (var qs = Store.QuerySession())
        {
            var home = await BuildSut(qs).GetExpensesHomeAsync(sc.HouseholdId, sc.Leo);
            await Assert.That(home.Expenses.Select(e => (e.Description, e.GroupName)))
                .IsEquivalentTo(new[] { ("Hotel", "Trip"), ("Groceries", "Home") });
        }

        await using (var s = Store.LightweightSession())
        {
            var doc = await s.LoadAsync<ExpenseGroupDocument>(sc.Trip);
            doc!.DeletedAt = DateTimeOffset.UtcNow;
            s.Store(doc);
            await s.SaveChangesAsync();
        }

        await using var qs2 = Store.QuerySession();
        var after = await BuildSut(qs2).GetExpensesHomeAsync(sc.HouseholdId, sc.Leo);
        await Assert.That(after.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Groceries" });
    }

    [Test]
    public async Task Home_shows_a_household_settle_once_with_the_cash_total()
    {
        var sc = await SeedTwoGroupsAsync();
        Guid leadId;
        await using (var s = Store.LightweightSession())
        {
            var commands = new ExpenseCommandService(
                s, new FakeCurrentUser(sc.Leo), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1)));
            leadId = await commands.RecordHouseholdSettlementAsync(sc.HouseholdId, sc.Leo, sc.Ana, 40, DateTimeOffset.UtcNow);
        }

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);

        // 40 of the 70 owed leaves the household unsettled: no marker, so the settle is Recent.
        var home = await sut.GetExpensesHomeAsync(sc.HouseholdId, sc.Leo);
        await Assert.That(home.HasOlder).IsFalse();
        var payment = home.Settlements.Single();
        await Assert.That(payment.Id).IsEqualTo(leadId);
        await Assert.That(payment.Cents).IsEqualTo(40L);
        await Assert.That(payment.IsHouseholdSettle).IsTrue();
        await Assert.That(payment.IsOffset).IsFalse();
        await Assert.That(payment.GroupName).IsNull();
    }

    [Test]
    public async Task Pages_follow_the_visual_order_with_payments_first_within_a_day()
    {
        var p = await SeedEmptyGroupAsync();
        // Same UTC day: the 08:00 payment still sorts above the 20:00 expense, as ExpenseTimeline.Merge shows it.
        var day = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        await AddExpenseAsync(p, "Late", day.AddHours(20), Day0, 100);
        await AddExpenseAsync(p, "Early", day.AddHours(1), Day0, 100);
        await AddPaymentAsync(p, day.AddHours(8), Day0, 1);
        await AddExpenseAsync(p, "Day before", day.AddDays(-1).AddHours(12), Day0, 100);

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var collected = new List<string>();
        string? cursor = null;
        do
        {
            var page = await sut.GetOlderTimelineAsync(p.HouseholdId, p.Group, cursor, pageSize: 1);
            collected.AddRange(ExpenseTimeline.Merge(page.Expenses, page.Settlements)
                .Select(x => x is ExpenseEntry e ? e.Expense.Description : "payment"));
            cursor = page.NextCursor;
        } while (cursor is not null);

        await Assert.That(collected).IsEquivalentTo(new[] { "payment", "Late", "Early", "Day before" });
        await Assert.That(collected.SequenceEqual(new[] { "payment", "Late", "Early", "Day before" })).IsTrue();
    }

    [Test]
    public async Task A_malformed_cursor_starts_Older_from_the_beginning()
    {
        var p = await SeedEmptyGroupAsync();
        await AddExpenseAsync(p, "Dinner", Day0, Day0);

        await using var qs = Store.QuerySession();
        var page = await BuildSut(qs).GetOlderTimelineAsync(p.HouseholdId, p.Group, "not-a-cursor!");

        await Assert.That(page.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Dinner" });
    }

    [Test]
    public async Task Home_ignores_the_household_marker_while_active_groups_still_owe()
    {
        var p = await SeedEmptyGroupAsync("Trip");
        var home = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = home, HouseholdId = p.HouseholdId, Name = "Home" });
            // Home: Leo owes Ana 50.
            var oldId = Guid.NewGuid();
            s.Events.Append(oldId, new ExpenseRecorded(Guid.NewGuid(), Day0, oldId, p.HouseholdId, home, "Old", Day0,
                [new FundingSource(p.Ana, 100)], [new Allocation(p.Ana, 50), new Allocation(p.Leo, 50)], p.Ana));
            await s.SaveChangesAsync();
        }

        // Trip: Ana owes Leo 50, so the household nets to zero and gets a marker.
        await AddExpenseAsync(p, "Dinner", Day0.AddDays(1), Day0.AddDays(1), 100);
        await using (var qs0 = Store.QuerySession())
        {
            var settled = await BuildSut(qs0).GetExpensesHomeAsync(p.HouseholdId, p.Leo);
            await Assert.That(settled.Balances).IsEmpty();
            await Assert.That(settled.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Dinner" });
            await Assert.That(settled.HasOlder).IsTrue();
        }

        // Deleting Trip leaves Home's 50 outstanding while the household marker still points at the settled moment.
        await using (var s = Store.LightweightSession())
        {
            var doc = await s.LoadAsync<ExpenseGroupDocument>(p.Group);
            doc!.DeletedAt = DateTimeOffset.UtcNow;
            s.Store(doc);
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var after = await BuildSut(qs).GetExpensesHomeAsync(p.HouseholdId, p.Leo);

        await Assert.That(after.Balances.Single().Cents).IsEqualTo(-50L);
        await Assert.That(after.Expenses.Select(e => e.Description)).IsEquivalentTo(new[] { "Old" });
        await Assert.That(after.HasOlder).IsFalse();
    }

    [Test]
    public async Task Recent_with_a_marker_is_capped_at_100_and_Older_continues_where_it_ends()
    {
        var p = await SeedEmptyGroupAsync();
        await AddExpenseAsync(p, "Before", Day0, Day0, 100);
        await AddPaymentAsync(p, Day0.AddDays(1), Day0.AddDays(1), 50);
        for (var i = 0; i < 105; i++)
            await AddExpenseAsync(p, $"After{i:D3}", Day0.AddDays(2).AddMinutes(i), Day0.AddDays(2).AddMinutes(i), 100);

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var page = await sut.GetGroupPageAsync(p.HouseholdId, p.Group, p.Leo);

        await Assert.That(page!.Expenses.Count + page.Settlements.Count).IsEqualTo(100);
        await Assert.That(page.HasOlder).IsTrue();
        await Assert.That(page.FirstOlderCursor).IsNotNull();

        var seen = page.Expenses.Select(e => e.Id).Concat(page.Settlements.Select(x => x.Id)).ToList();
        var cursor = page.FirstOlderCursor;
        var pages = 0;
        do
        {
            var next = await sut.GetOlderTimelineAsync(p.HouseholdId, p.Group, cursor, pageSize: 4);
            seen.AddRange(next.Expenses.Select(e => e.Id));
            seen.AddRange(next.Settlements.Select(x => x.Id));
            cursor = next.NextCursor;
            pages++;
        } while (cursor is not null && pages < 20);

        // 105 expenses after the marker, the settle itself, and the one expense before it.
        await Assert.That(cursor).IsNull();
        await Assert.That(seen.Count).IsEqualTo(107);
        await Assert.That(seen.Distinct().Count()).IsEqualTo(107);
    }

    [Test]
    public async Task Icons_reach_cards_chips_group_page_preview_and_group_list()
    {
        var sc = await SeedTwoGroupsAsync();
        await using (var s = Store.LightweightSession())
        {
            var trip = await s.LoadAsync<ExpenseGroupDocument>(sc.Trip);
            trip!.Icon = "✈️";
            s.Store(trip);
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var home = await sut.GetExpensesHomeAsync(sc.HouseholdId, sc.Leo);
        await Assert.That(home.Groups.Single(g => g.Id == sc.Trip).Icon).IsEqualTo("✈️");
        await Assert.That(home.Groups.Single(g => g.Id == sc.Home).Icon).IsEqualTo(GroupIcons.Default);
        await Assert.That(home.Expenses.Single(e => e.Description == "Hotel").GroupIcon).IsEqualTo("✈️");

        var page = await sut.GetGroupPageAsync(sc.HouseholdId, sc.Trip, sc.Leo);
        await Assert.That(page!.Icon).IsEqualTo("✈️");

        var preview = await sut.PreviewHouseholdSettlementAsync(sc.HouseholdId, sc.Leo, sc.Ana, 70);
        await Assert.That(preview.Legs.Single(l => l.GroupId == sc.Trip).GroupIcon).IsEqualTo("✈️");

        var groups = await sut.ListExpenseGroupsAsync(sc.HouseholdId);
        await Assert.That(groups.Single(g => g.Id == sc.Trip).Icon).IsEqualTo("✈️");
    }

    [Test]
    public async Task Expense_sheet_names_the_people_and_stays_inside_its_household()
    {
        var householdId = Guid.NewGuid();
        Guid payerId = Guid.NewGuid(), debtorId = Guid.NewGuid();
        await InsertUsersAsync(payerId, debtorId);
        var expenseId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = "Home", Icon = "🏠" });
            s.Events.Append(expenseId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
                [new FundingSource(payerId, 1000)], [new Allocation(payerId, 500), new Allocation(debtorId, 500)], payerId));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var sut = BuildSut(qs);
        var sheet = await sut.GetExpenseSheetAsync(householdId, expenseId);
        var elsewhere = await sut.GetExpenseSheetAsync(Guid.NewGuid(), expenseId);

        await Assert.That(sheet!.GroupId).IsEqualTo(groupId);
        await Assert.That(sheet.Expense.GroupIcon).IsEqualTo("🏠");
        await Assert.That(sheet.Expense.FundingSources.Single().DisplayName).IsEqualTo($"User {payerId}");
        await Assert.That(sheet.Expense.Allocations.Count).IsEqualTo(2);
        await Assert.That(elsewhere).IsNull();
    }
}

