using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Domain;
using Dapper;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(MemberRemovalTests))]
public sealed class MemberRemovalTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _householdId = Guid.NewGuid();
    private readonly Guid _groupId = Guid.NewGuid();
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _bia = Guid.NewGuid();
    private readonly Guid _leo = Guid.NewGuid();

    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString, "expenses_member_removal_test");

    private ExpenseMemberRemovalService Sut(IDocumentSession session) => new(
        session,
        new ExpenseQueryService(session, new UserRepository(db.DataSource, TimeProvider.System, Substitute.For<IEventBus>()), Substitute.For<IHouseholdQueries>(), TimeProvider.System),
        new FakeCurrentUser(_ana), new FakeTimeProvider(Now));

    private async Task InsertUsersAsync()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        foreach (var id in new[] { _ana, _bia, _leo })
            await conn.ExecuteAsync(
                "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
                new { id, sub = $"sub-{id}", email = $"{id}@test.com", name = $"User {id}" });
    }

    private async Task<Guid> CreateTemplateAsync(string description, Guid payer, (Guid User, long Cents)[] shares)
    {
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, _groupId);
        var template = RecurringExpense.Create(
            _householdId, _groupId, description,
            [new FundingSource(payer, shares.Sum(s => s.Cents))],
            shares.Select(s => new Allocation(s.User, s.Cents)).ToList(),
            new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 12, 1), new TimeOnly(9, 0)),
            false, Now, _ana);
        await using var session = Store.LightweightSession();
        session.Events.StartStream(template.Id, template.DomainEvents.ToArray());
        await session.SaveChangesAsync();
        return template.Id;
    }

    private async Task<RecurringExpenseDocument> DocumentAsync(Guid id)
    {
        await using var qs = Store.QuerySession();
        return (await qs.LoadAsync<RecurringExpenseDocument>(id))!;
    }

    [Test]
    public async Task Apply_redistributes_shares_ends_templates_they_pay_and_previews_the_same()
    {
        var rent = await CreateTemplateAsync("Rent", _ana, [(_ana, 100_00), (_bia, 100_00), (_leo, 100_00)]);
        var netflix = await CreateTemplateAsync("Netflix", _bia, [(_ana, 30_00), (_bia, 30_00)]);

        await using (var session = Store.LightweightSession())
        {
            var preview = await Sut(session).PreviewAsync(_householdId, _bia);
            await Assert.That(preview.Recurring.Single(r => r.RecurringExpenseId == rent).Effect)
                .IsEqualTo(RecurringRemovalEffect.Redistributed);
            await Assert.That(preview.Recurring.Single(r => r.RecurringExpenseId == netflix).Effect)
                .IsEqualTo(RecurringRemovalEffect.Ended);
        }

        await using (var session = Store.LightweightSession())
            await Sut(session).ApplyAsync(_householdId, _bia);

        var rentDoc = await DocumentAsync(rent);
        await Assert.That(rentDoc.Allocations.Select(a => (a.UserId, a.Cents)))
            .IsEquivalentTo(new[] { (_ana, 150_00L), (_leo, 150_00L) });
        await Assert.That((await DocumentAsync(netflix)).Status).IsEqualTo(RecurringExpenseStatus.Ended);
    }

    [Test]
    public async Task Preview_reports_the_members_net_balance_and_plans()
    {
        await InsertUsersAsync();
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, _groupId);
        var expenseId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(expenseId, new ExpenseRecorded(
                Guid.NewGuid(), Now, expenseId, _householdId, _groupId, "Groceries", Now,
                [new FundingSource(_ana, 100_00)],
                [new Allocation(_ana, 50_00), new Allocation(_bia, 50_00)], _ana));
            s.Store(new InstallmentPlanDocument
            {
                Id = planId, HouseholdId = _householdId, ExpenseGroupId = _groupId, Description = "TV",
                PayerId = _ana, Status = InstallmentPlanStatus.Active,
                Segments = [new SplitSegment(1, [new Allocation(_ana, 50_00), new Allocation(_bia, 50_00)])]
            });
            await s.SaveChangesAsync();
        }

        await using var session = Store.LightweightSession();
        var preview = await Sut(session).PreviewAsync(_householdId, _bia);

        await Assert.That(preview.NetCents).IsEqualTo(-50_00L);
        await Assert.That(preview.InstallmentPlans).Contains("TV");
    }
}
