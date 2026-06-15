using Cronos;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Jobs;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;
using Marten;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public sealed class ExpenseCommandService(
    IDocumentSession session,
    IEventBus eventBus,
    IRecurringExpenseRepository recurringRepo,
    IRecurringJobScheduler scheduler,
    IUnitOfWork uow,
    TimeProvider time) : IExpenseCommands
{
    public async Task<Guid> RecordExpenseAsync(
        Guid householdId, Guid expenseGroupId, string description, DateTimeOffset date,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default)
    {
        var expense = Expense.Record(
            householdId, expenseGroupId, description, date,
            fundingSources.Select(f => new FundingSource(f.UserId, f.Cents)).ToList(),
            allocations.Select(a => new Allocation(a.UserId, a.Cents)).ToList(),
            time.GetUtcNow());

        session.Events.Append(expense.Id, expense.DomainEvents.Cast<object>().ToArray());
        await session.SaveChangesAsync(ct);
        await eventBus.PublishAllAsync(expense, ct);
        return expense.Id;
    }

    public async Task VoidExpenseAsync(Guid expenseId, string? reason, CancellationToken ct = default)
    {
        var expense = await session.Events.AggregateStreamAsync<Expense>(expenseId, token: ct)
            ?? throw new InvalidOperationException("Expense not found.");
        expense.Void(reason, time.GetUtcNow());
        session.Events.Append(expenseId, expense.DomainEvents.Cast<object>().ToArray());
        await session.SaveChangesAsync(ct);
        await eventBus.PublishAllAsync(expense, ct);
    }

    public async Task<Guid> RecordSettlementAsync(
        Guid householdId, Guid payerId, Guid recipientId, long cents, DateTimeOffset date,
        CancellationToken ct = default)
    {
        var settlementId = Guid.NewGuid();
        var @event = new SettlementRecorded(
            Guid.NewGuid(), time.GetUtcNow(), settlementId,
            householdId, payerId, recipientId, cents, date);

        session.Events.Append(settlementId, @event);
        await session.SaveChangesAsync(ct);
        await eventBus.PublishAsync(@event, ct);
        return settlementId;
    }

    public async Task<Guid> CreateExpenseGroupAsync(
        Guid householdId, string name, string? description, CancellationToken ct = default)
    {
        var group = ExpenseGroup.Create(householdId, name, description, [], time.GetUtcNow());
        var doc = new ExpenseGroupDocument { Id = group.Id, HouseholdId = group.HouseholdId, Name = group.Name, Description = group.Description };
        session.Store(doc);
        session.Events.Append(group.Id, group.DomainEvents.Cast<object>().ToArray());
        await session.SaveChangesAsync(ct);
        await eventBus.PublishAllAsync(group, ct);
        return group.Id;
    }

    public async Task DeleteExpenseGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        var doc = await session.LoadAsync<ExpenseGroupDocument>(groupId, ct)
            ?? throw new InvalidOperationException("Expense group not found.");

        var hasActiveExpenses = await session.Query<ExpenseReadModel>()
            .Where(e => e.ExpenseGroupId == groupId && !e.IsVoided)
            .AnyAsync(ct);
        if (hasActiveExpenses)
            throw new InvalidOperationException("Cannot delete an expense group with active expenses.");

        var @event = new ExpenseGroupDeleted(Guid.NewGuid(), time.GetUtcNow(), groupId, doc.HouseholdId);
        session.Events.Append(groupId, @event);
        session.Delete<ExpenseGroupDocument>(groupId);
        await session.SaveChangesAsync(ct);
    }

    public async Task<Guid> CreateRecurringExpenseAsync(
        Guid householdId, Guid expenseGroupId, string description,
        string cronExpression,
        IReadOnlyList<FundingSourceDto> defaultFundingSources,
        IReadOnlyList<AllocationDto> defaultAllocations,
        CancellationToken ct = default)
    {
        var cron = CronExpression.Parse(cronExpression);
        var nextRun = cron.GetNextOccurrence(time.GetUtcNow(), TimeZoneInfo.Utc);

        var recurring = RecurringExpense.Create(
            householdId, expenseGroupId, description,
            defaultFundingSources.Select(f => new FundingSource(f.UserId, f.Cents)).ToList(),
            defaultAllocations.Select(a => new Allocation(a.UserId, a.Cents)).ToList(),
            cronExpression, nextRun);

        var schedulerJobId = await scheduler.ScheduleCronAsync(
            RecurringExpenseJobs.FunctionName, cronExpression, recurring.Id, ct);
        recurring.SetSchedulerJobId(schedulerJobId);

        try
        {
            await uow.BeginTransactionAsync(ct);
            await recurringRepo.SaveAsync(recurring, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await scheduler.UnscheduleCronAsync(schedulerJobId, ct);
            throw;
        }

        return recurring.Id;
    }

    public async Task SpawnRecurringExpenseAsync(Guid recurringExpenseId, CancellationToken ct = default)
    {
        var recurring = await recurringRepo.GetAsync(recurringExpenseId, ct);
        if (recurring is null || !recurring.IsActive) return;

        var expense = recurring.Spawn(time.GetUtcNow());
        session.Events.Append(expense.Id, expense.DomainEvents.Cast<object>().ToArray());
        await session.SaveChangesAsync(ct);
        await eventBus.PublishAllAsync(expense, ct);
    }

    public async Task DeactivateRecurringExpenseAsync(Guid recurringExpenseId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var recurring = await recurringRepo.GetAsync(recurringExpenseId, ct)
            ?? throw new InvalidOperationException("Recurring expense not found.");

        recurring.Deactivate();
        await recurringRepo.SaveAsync(recurring, ct);

        if (recurring.SchedulerJobId.HasValue)
            await scheduler.UnscheduleCronAsync(recurring.SchedulerJobId.Value, ct);

        await uow.CommitAsync(ct);
    }
}
