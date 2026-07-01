using System.Collections.Concurrent;
using Dapper;
using TUnit.Core.Interfaces;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using HouseholdApp.IntegrationTests.Infrastructure;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseQueryServiceTests(PostgresFixture db)
{
    private static readonly ConcurrentDictionary<string, Lazy<IDocumentStore>> _stores = new();

    private IDocumentStore Store => _stores
        .GetOrAdd(db.ConnectionString, cs => new Lazy<IDocumentStore>(() => DocumentStore.For(opts =>
        {
            opts.Connection(cs);
            opts.DatabaseSchemaName = "expenses";
            opts.Events.AddEventType<ExpenseRecorded>();
            opts.Events.AddEventType<ExpenseVoided>();
            opts.Events.AddEventType<SettlementRecorded>();
            opts.Projections.Add<ExpenseReadModelProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<HouseholdLedgerProjection>(ProjectionLifecycle.Inline);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        })))
        .Value;

    private ExpenseQueryService BuildSut(IQuerySession querySession) =>
        new(querySession, db.DataSource, new UserRepository(db.DataSource, TimeProvider.System));

    [Test]
    public async Task GetExpensesSummaryAsync_returns_empty_for_unknown_household()
    {
        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(Guid.NewGuid());

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
                [new Allocation(payerId, 500), new Allocation(splitUserId, 500)]));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId);

        await Assert.That(summary.Expenses.Count).IsEqualTo(1);
        var expense = summary.Expenses[0];
        await Assert.That(expense.Description).IsEqualTo("Dinner");
        await Assert.That(expense.TotalCents).IsEqualTo(1000L);
        await Assert.That(expense.FundingSources.Count).IsEqualTo(1);
        await Assert.That(expense.FundingSources[0].DisplayName).IsEqualTo("Alice");
        await Assert.That(expense.FundingSources[0].Cents).IsEqualTo(1000L);
        await Assert.That(expense.Allocations.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetExpensesSummaryAsync_returns_balances_from_ledger()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payerId = Guid.NewGuid();
        var debtorId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id = payerId, sub = $"sub-{payerId}", email = "payer@test.com", name = "Payer" });
        await conn.ExecuteAsync(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @sub, @email, @name)",
            new { id = debtorId, sub = $"sub-{debtorId}", email = "debtor@test.com", name = "Debtor" });

        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(expenseId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, groupId,
                "Groceries", DateTimeOffset.UtcNow,
                [new FundingSource(payerId, 1000)],
                [new Allocation(payerId, 500), new Allocation(debtorId, 500)]));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId);

        await Assert.That(summary.Balances.Count).IsEqualTo(2);
        var payerBalance = summary.Balances.Single(b => b.UserId == payerId);
        var debtorBalance = summary.Balances.Single(b => b.UserId == debtorId);
        await Assert.That(payerBalance.Cents).IsEqualTo(500L);
        await Assert.That(debtorBalance.Cents).IsEqualTo(-500L);
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
                [new Allocation(userId, 500)]));
            s.Events.Append(expenseB, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseB, householdId, groupB,
                "GroupB Expense", DateTimeOffset.UtcNow,
                [new FundingSource(userId, 300)],
                [new Allocation(userId, 300)]));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var summary = await BuildSut(qs).GetExpensesSummaryAsync(householdId, groupId: groupA);

        await Assert.That(summary.Expenses.Count).IsEqualTo(1);
        await Assert.That(summary.Expenses[0].Description).IsEqualTo("GroupA Expense");
    }
}
