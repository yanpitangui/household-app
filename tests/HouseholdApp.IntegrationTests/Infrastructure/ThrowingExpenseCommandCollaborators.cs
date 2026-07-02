using System.Data;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;

namespace HouseholdApp.IntegrationTests.Infrastructure;

/// <summary>
/// Fake collaborators for <see cref="ExpenseCommandServiceVoidTests"/> and
/// <see cref="ExpenseCommandServiceEditTests"/>-style tests that only exercise the Marten-backed
/// paths of ExpenseCommandService. All members throw if invoked, since those tests never reach
/// the recurring-expense or unit-of-work code paths.
/// </summary>
public sealed class ThrowingRecurringExpenseRepository : IRecurringExpenseRepository
{
    public Task<RecurringExpense?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveAsync(RecurringExpense expense, CancellationToken ct = default) => throw new NotSupportedException();
}

public sealed class ThrowingRecurringJobScheduler : IRecurringJobScheduler
{
    public Task<Guid> ScheduleCronAsync(string functionName, string cronExpression, Guid entityId, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task UnscheduleCronAsync(Guid schedulerJobId, CancellationToken ct = default) => throw new NotSupportedException();
}

public sealed class ThrowingUnitOfWork : IUnitOfWork
{
    public IDbTransaction? CurrentTransaction => null;
    public Task<IDbConnection> GetConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task BeginTransactionAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task CommitAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
