using Dapper;
using TUnit.Core.Interfaces;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseQueryServiceTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private ExpenseQueryService BuildSut(IQuerySession querySession) =>
        new(querySession, db.DataSource, new UserRepository(db.DataSource, TimeProvider.System, Substitute.For<IEventBus>()));

    [Test]
    public async Task GetExpensesSummaryAsync_returns_empty_for_unknown_household()
    {
        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(Guid.NewGuid(), Guid.NewGuid());

        await Assert.That(summary.Expenses).IsEmpty();
        await Assert.That(summary.Balances).IsEmpty();
    }

    [Test]
    public async Task GetExpensesSummaryAsync_returns_expenses_with_participants()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payerId = Guid.NewGuid();
        var splitUserId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id = payerId, sub = $"sub-{payerId}", email = "alice@test.com", name = "Alice" });
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id = splitUserId, sub = $"sub-{splitUserId}", email = "bob@test.com", name = "Bob" });

        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(expenseId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, groupId,
                "Dinner", DateTimeOffset.UtcNow,
                [new FundingSource(payerId, 1000)],
                [new Allocation(payerId, 500), new Allocation(splitUserId, 500)],
                payerId));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId, Guid.NewGuid());

        await Assert.That(summary.Expenses.Count).IsEqualTo(1);
        var expense = summary.Expenses[0];
        await Assert.That(expense.Description).IsEqualTo("Dinner");
        await Assert.That(expense.TotalCents).IsEqualTo(1000L);
        await Assert.That(expense.FundingSources.Count).IsEqualTo(1);
        await Assert.That(expense.FundingSources[0].DisplayName).IsEqualTo("Alice");
        await Assert.That(expense.FundingSources[0].Cents).IsEqualTo(1000L);
        await Assert.That(expense.Allocations.Count).IsEqualTo(2);
    }

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
        var share = cents / splitBetween.Length;
        await using var s = Store.LightweightSession();
        s.Events.Append(expenseId, new ExpenseRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, Guid.NewGuid(),
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
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId, payerId);

        var only = summary.Balances.Single();
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
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId, debtorId);

        var only = summary.Balances.Single();
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
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId, b);

        var only = summary.Balances.Single();
        await Assert.That(only.UserId).IsEqualTo(a);
        await Assert.That(only.Cents).IsEqualTo(-300L);
    }

    [Test]
    public async Task ListRecurringExpensesAsync_returns_empty_for_unknown_household()
    {
        await using var qs = Store.QuerySession();
        var results = await BuildSut(qs).ListRecurringExpensesAsync(Guid.NewGuid());

        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task ListRecurringExpensesAsync_returns_recurring_expenses_with_correct_fields()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var startAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var cron = RecurringExpense.ComputeCron(RecurrenceFrequency.Monthly, startAt);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO expenses.recurring_expenses
                (household_id, expense_group_id, description, recurrence_frequency, start_at, cron_expression, is_active, funding_sources, allocations)
            VALUES
                (@HouseholdId, @GroupId, 'Rent', 'Monthly', @StartAt, @Cron, true, '[]', '[]')
            """,
            new { HouseholdId = householdId, GroupId = groupId, StartAt = startAt, Cron = cron });

        await using var qs = Store.QuerySession();
        var results = await BuildSut(qs).ListRecurringExpensesAsync(householdId);

        await Assert.That(results.Count).IsEqualTo(1);
        var item = results[0];
        await Assert.That(item.Description).IsEqualTo("Rent");
        await Assert.That(item.Frequency).IsEqualTo(RecurrenceFrequency.Monthly);
        await Assert.That(item.StartAt).IsEqualTo(startAt);
        await Assert.That(item.IsActive).IsTrue();
        await Assert.That(item.ExpenseGroupId).IsEqualTo(groupId);
    }

    [Test]
    public async Task ListRecurringExpensesAsync_only_returns_expenses_for_requested_household()
    {
        var householdA = Guid.NewGuid();
        var householdB = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var startAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var cron = RecurringExpense.ComputeCron(RecurrenceFrequency.Annually, startAt);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO expenses.recurring_expenses
                (household_id, expense_group_id, description, recurrence_frequency, start_at, cron_expression, is_active, funding_sources, allocations)
            VALUES
                (@H, @G, 'A-expense', 'Annually', @S, @C, true, '[]', '[]'),
                (@HB, @G, 'B-expense', 'Annually', @S, @C, true, '[]', '[]')
            """,
            new { H = householdA, HB = householdB, G = groupId, S = startAt, C = cron });

        await using var qs = Store.QuerySession();
        var results = await BuildSut(qs).ListRecurringExpensesAsync(householdA);

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results[0].Description).IsEqualTo("A-expense");
    }

    [Test]
    public async Task GetExpensesSummaryAsync_filters_by_groupId()
    {
        var householdId = Guid.NewGuid();
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expenseA = Guid.NewGuid();
        var expenseB = Guid.NewGuid();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id = userId, sub = $"sub-{userId}", email = "user@test.com", name = "User" });

        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(expenseA, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseA, householdId, groupA,
                "GroupA Expense", DateTimeOffset.UtcNow,
                [new FundingSource(userId, 500)],
                [new Allocation(userId, 500)],
                userId));
            s.Events.Append(expenseB, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseB, householdId, groupB,
                "GroupB Expense", DateTimeOffset.UtcNow,
                [new FundingSource(userId, 300)],
                [new Allocation(userId, 300)],
                userId));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId, Guid.NewGuid(), groupId: groupA);

        await Assert.That(summary.Expenses.Count).IsEqualTo(1);
        await Assert.That(summary.Expenses[0].Description).IsEqualTo("GroupA Expense");
    }

    [Test]
    public async Task GetExpensesSummaryAsync_returns_expenses_ordered_newest_date_first()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var oldestId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var newestId = Guid.NewGuid();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id = userId, sub = $"sub-{userId}", email = "orderuser@test.com", name = "OrderUser" });

        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(oldestId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, oldestId, householdId, groupId,
                "Oldest", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                [new FundingSource(userId, 100)], [new Allocation(userId, 100)], userId));
            s.Events.Append(newestId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, newestId, householdId, groupId,
                "Newest", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
                [new FundingSource(userId, 100)], [new Allocation(userId, 100)], userId));
            s.Events.Append(middleId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, middleId, householdId, groupId,
                "Middle", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                [new FundingSource(userId, 100)], [new Allocation(userId, 100)], userId));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId, Guid.NewGuid(), groupId: groupId);

        await Assert.That(summary.Expenses.Count).IsEqualTo(3);
        await Assert.That(summary.Expenses[0].Description).IsEqualTo("Newest");
        await Assert.That(summary.Expenses[1].Description).IsEqualTo("Middle");
        await Assert.That(summary.Expenses[2].Description).IsEqualTo("Oldest");
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

        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].Name).IsEqualTo("Apple Groceries");
        await Assert.That(groups[1].Name).IsEqualTo("Zebra Trip");
    }
}
