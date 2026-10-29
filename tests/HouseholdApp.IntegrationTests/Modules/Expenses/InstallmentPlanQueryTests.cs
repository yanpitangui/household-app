using Dapper;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class InstallmentPlanQueryTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _householdId = Guid.NewGuid();
    private readonly Guid _groupId = Guid.NewGuid();
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _leo = Guid.NewGuid();

    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private ExpenseQueryService Queries(IQuerySession qs) =>
        new(qs, db.DataSource, new UserRepository(db.DataSource, TimeProvider.System, Substitute.For<IEventBus>()),
            new FakeTimeProvider(Now));

    private async Task InsertUsersAsync(params Guid[] ids)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        foreach (var id in ids)
            await conn.ExecuteAsync(
                "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name) ON CONFLICT DO NOTHING",
                new { id, sub = $"sub-{id}", email = $"{id}@test.com", name = $"User {id}" });
    }

    private async Task<Guid> CreatePlanAsync(
        DateOnly firstDate, Guid? groupId = null, string description = "Car repair", bool seedGroup = true,
        long downPaymentCents = 0, DateOnly? purchaseDate = null)
    {
        var group = groupId ?? _groupId;
        await InsertUsersAsync(_ana, _leo);
        if (seedGroup) await ExpenseTestData.SeedGroupAsync(Store, _householdId, group, "Car");
        await using var session = Store.LightweightSession();
        var sut = new InstallmentPlanCommandService(
            session, new FakeCurrentUser(_ana), new FakeTimeProvider(Now), Substitute.For<IEventBus>(),
            Substitute.For<ISessionFactory>(), NullLogger<InstallmentPlanCommandService>.Instance);
        return await sut.CreateAsync(_householdId, group, description, _ana, 150000, downPaymentCents, 5, purchaseDate ?? firstDate, firstDate,
            [new AllocationDto(_ana, 75000), new AllocationDto(_leo, 75000)]);
    }

    private async Task InsertTemplateAsync(Guid groupId, string description, bool active = true)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO expenses.recurring_expenses
              (household_id, expense_group_id, description, recurrence_frequency, start_at, cron_expression,
               is_active, funding_sources, allocations)
            VALUES (@h, @g, @d, 'Monthly', now(), '0 9 20 * *', @active, @f::jsonb, @a::jsonb)
            """,
            new
            {
                h = _householdId, g = groupId, d = description, active,
                f = $"[{{\"UserId\":\"{_leo}\",\"Cents\":12000}}]",
                a = $"[{{\"UserId\":\"{_ana}\",\"Cents\":6000}},{{\"UserId\":\"{_leo}\",\"Cents\":6000}}]"
            });
    }

    [Test]
    public async Task Plan_page_shows_slots_split_and_viewer_share()
    {
        var planId = await CreatePlanAsync(new DateOnly(2026, 10, 10));

        await using var qs = Store.QuerySession();
        var page = await Queries(qs).GetInstallmentPlanPageAsync(_householdId, planId, _leo);

        await Assert.That(page!.Summary.CreatedCents).IsEqualTo(60000L);
        await Assert.That(page.NextNumber).IsEqualTo(3);
        await Assert.That(page.ViewerShareCents).IsEqualTo(15000L);
        await Assert.That(page.GroupName).IsEqualTo("Car");
        // Three installments remain, each split 50/50 between Ana and Leo.
        await Assert.That(page.RemainingShares.Count).IsEqualTo(2);
        await Assert.That(page.RemainingShares.Sum(r => r.Cents)).IsEqualTo(page.Summary.RemainingCents);
        await Assert.That(page.RemainingShares.Single(r => r.UserId == _leo).Cents).IsEqualTo(45000L);
        await Assert.That(page.RemainingShares.Select(r => r.UserId).ToList())
            .IsEquivalentTo(page.RemainingShares.Select(r => r.UserId).OrderBy(x => x).ToList(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Plan_page_with_a_created_down_payment_keeps_it_out_of_remaining_and_the_monthly_share()
    {
        var planId = await CreatePlanAsync(new DateOnly(2026, 12, 10), downPaymentCents: 50000,
            purchaseDate: new DateOnly(2026, 11, 10));

        await using var qs = Store.QuerySession();
        var page = await Queries(qs).GetInstallmentPlanPageAsync(_householdId, planId, _leo);

        await Assert.That(page!.DownPaymentCents).IsEqualTo(50000L);
        await Assert.That(page.Summary.Slots[0].Number).IsEqualTo(0);
        await Assert.That(page.Summary.Slots[0].Status).IsEqualTo(InstallmentSlotStatus.Created);
        await Assert.That(page.Summary.CreatedCents).IsEqualTo(50000L);
        await Assert.That(page.Summary.RemainingCents).IsEqualTo(100000L);
        await Assert.That(page.NextNumber).IsEqualTo(1);
        await Assert.That(page.ViewerShareCents).IsEqualTo(10000L);
        await Assert.That(page.RemainingShares.Sum(r => r.Cents)).IsEqualTo(100000L);
    }

    [Test]
    public async Task Plan_page_with_a_future_down_payment_shows_the_monthly_share_not_the_down_share()
    {
        var planId = await CreatePlanAsync(new DateOnly(2026, 12, 20), downPaymentCents: 50000,
            purchaseDate: new DateOnly(2026, 11, 20));

        await using var qs = Store.QuerySession();
        var page = await Queries(qs).GetInstallmentPlanPageAsync(_householdId, planId, _leo);

        await Assert.That(page!.Summary.Slots[0].Status).IsEqualTo(InstallmentSlotStatus.Upcoming);
        await Assert.That(page.NextNumber).IsEqualTo(0);
        await Assert.That(page.ViewerShareCents).IsEqualTo(10000L);
        await Assert.That(page.Summary.RemainingCents).IsEqualTo(150000L);
        await Assert.That(page.RemainingShares.Sum(s => s.Cents)).IsEqualTo(150000L);
    }

    [Test]
    public async Task Timeline_rows_carry_the_link_and_the_plan_summary()
    {
        var planId = await CreatePlanAsync(new DateOnly(2026, 10, 10));

        await using var qs = Store.QuerySession();
        var page = await Queries(qs).GetGroupPageAsync(_householdId, _groupId, _ana);

        var row = page!.Expenses.First(e => e.Installment?.Number == 2);
        await Assert.That(row.Plan!.PlanId).IsEqualTo(planId);
        await Assert.That(row.Plan.Slots.Count).IsEqualTo(5);
    }

    [Test]
    public async Task Plan_page_hides_other_households()
    {
        var planId = await CreatePlanAsync(new DateOnly(2026, 10, 10));
        await using var qs = Store.QuerySession();
        await Assert.That(await Queries(qs).GetInstallmentPlanPageAsync(Guid.NewGuid(), planId, _ana)).IsNull();
    }

    [Test]
    public async Task Upcoming_lists_plan_and_template_rows_in_the_window()
    {
        await CreatePlanAsync(new DateOnly(2026, 10, 10)); // #3 due Dec 10: out of window from Nov 10
        await using (var conn = await db.DataSource.OpenConnectionAsync())
            await conn.ExecuteAsync(
                """
                INSERT INTO expenses.recurring_expenses
                  (household_id, expense_group_id, description, recurrence_frequency, start_at, cron_expression,
                   is_active, funding_sources, allocations)
                VALUES (@h, @g, 'Internet', 'Monthly', now(), '0 9 20 * *', true,
                        @f::jsonb, @a::jsonb)
                """,
                new
                {
                    h = _householdId, g = _groupId,
                    f = $"[{{\"UserId\":\"{_leo}\",\"Cents\":12000}}]",
                    a = $"[{{\"UserId\":\"{_ana}\",\"Cents\":6000}},{{\"UserId\":\"{_leo}\",\"Cents\":6000}}]"
                });

        await using var qs = Store.QuerySession();
        var summary = await Queries(qs).GetUpcomingAsync(_householdId, null, _ana);

        await Assert.That(summary.Items.Select(i => i.Description)).IsEquivalentTo(new[] { "Internet" });
        await Assert.That(summary.ViewerShareCents).IsEqualTo(6000L);
        await Assert.That(summary.Items[0].PayerName).IsNotNull();
    }

    [Test]
    public async Task Upcoming_skips_paused_templates_and_deleted_groups_and_scopes_to_one_group()
    {
        var groupB = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, groupB, "Home");
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, deleted, "Old");

        var firstDate = new DateOnly(2026, 11, 20); // #1 is due inside the window and not created yet
        await CreatePlanAsync(firstDate, _groupId, "Plan A");
        await CreatePlanAsync(firstDate, groupB, "Plan B", seedGroup: false);
        await CreatePlanAsync(firstDate, deleted, "Plan Gone", seedGroup: false);
        await InsertTemplateAsync(_groupId, "Internet A");
        await InsertTemplateAsync(groupB, "Internet B");
        await InsertTemplateAsync(_groupId, "Paused", active: false);
        await InsertTemplateAsync(deleted, "Template Gone");
        await using (var s = Store.LightweightSession())
        {
            // Soft-deleted after the plan exists: creating a plan in a deleted group is rejected.
            var group = (await s.LoadAsync<ExpenseGroupDocument>(deleted))!;
            group.DeletedAt = Now;
            s.Store(group);
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var all = await Queries(qs).GetUpcomingAsync(_householdId, null, _ana);
        var onlyB = await Queries(qs).GetUpcomingAsync(_householdId, groupB, _ana);

        await Assert.That(all.Items.Select(i => i.Description).Order())
            .IsEquivalentTo(new[] { "Internet A", "Internet B", "Plan A", "Plan B" });
        await Assert.That(all.TotalCents).IsEqualTo(84000L);
        await Assert.That(all.ViewerShareCents).IsEqualTo(42000L);
        await Assert.That(onlyB.Items.Select(i => i.Description).Order())
            .IsEquivalentTo(new[] { "Internet B", "Plan B" });
        await Assert.That(onlyB.TotalCents).IsEqualTo(42000L);
        await Assert.That(onlyB.ViewerShareCents).IsEqualTo(21000L);
    }
}
