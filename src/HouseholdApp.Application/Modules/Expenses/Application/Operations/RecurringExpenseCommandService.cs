using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

/// <summary>
/// Every operation commits the template events and the Expenses they create in one SaveChangesAsync. Operations and the
/// sweep serialize on the template's stream lock.
/// </summary>
public sealed class RecurringExpenseCommandService(
    IDocumentSession session,
    ICurrentUser currentUser,
    TimeProvider time,
    IEventBus eventBus,
    ISessionFactory sessionFactory,
    ILogger<RecurringExpenseCommandService> logger) : IRecurringExpenseCommands
{
    public async Task<Guid> CreateAsync(
        Guid householdId, Guid groupId, string description, RecurrenceSchedule schedule,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations, bool catchUp,
        CancellationToken ct = default)
    {
        await EnsureActiveOwnedGroupAsync(householdId, groupId, ct);
        var now = time.GetUtcNow();
        var template = RecurringExpense.Create(
            householdId, groupId, description, ToDomain(fundingSources), ToDomain(allocations), schedule, catchUp, now,
            currentUser.Id);

        // A past catch-up and today's already-due first occurrence are both committed with their template.
        var startsInThePast = schedule.FirstDate < DateOnly.FromDateTime(now.UtcDateTime);
        var catchUps = new List<Expense>();
        var firstOccurrenceIsDue = schedule.FirstDate == DateOnly.FromDateTime(now.UtcDateTime) && schedule.At(0) <= now;
        if ((catchUp && startsInThePast) || firstOccurrenceIsDue)
        {
            foreach (var date in template.DueOccurrences(now))
            {
                catchUps.Add(template.BuildExpense(date, now, coveredByCreatedEvent: true));
                template.MarkOccurred(date, now);
            }
        }

        session.Events.StartStream(template.Id, template.DomainEvents.ToArray());
        foreach (var expense in catchUps)
            session.Events.StartStream(expense.Id, expense.DomainEvents.ToArray());

        await session.SaveChangesAsync(ct);
        return template.Id;
    }

    public async Task UpdateAsync(
        Guid householdId, Guid recurringExpenseId, string description, RecurrenceSchedule schedule,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default)
    {
        var (stream, template) = await LoadAsync(householdId, recurringExpenseId, ct);
        template.Update(description, ToDomain(fundingSources), ToDomain(allocations), schedule, time.GetUtcNow(), currentUser.Id);
        stream.AppendMany(template.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
    }

    public async Task EndAsync(Guid householdId, Guid recurringExpenseId, CancellationToken ct = default)
    {
        var (stream, template) = await LoadAsync(householdId, recurringExpenseId, ct);
        template.End(time.GetUtcNow(), currentUser.Id);
        stream.AppendMany(template.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
    }

    public async Task RunSweepAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var ids = await session.Query<RecurringExpenseDocument>()
            .Where(t => t.Status == RecurringExpenseStatus.Active && t.NextOccurrenceAt <= now)
            .OrderBy(t => t.NextOccurrenceAt).ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(ct);

        // One session per template, so the lock and transaction end with the template: a skipped, locked or failed one
        // never holds a lock or leaves an aborted transaction for the ones after it.
        foreach (var id in ids)
        {
            try
            {
                await using var templateSession = sessionFactory.OpenSession();
                await SweepTemplateAsync(templateSession, id, now, ct);
            }
            catch (ExistingStreamIdCollisionException)
            {
                eventBus.Discard();
                logger.LogInformation("Recurring expense {RecurringExpenseId} was swept concurrently", id);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                eventBus.Discard();
                logger.LogError(ex, "Sweeping recurring expense {RecurringExpenseId} failed", id);
            }
        }
    }

    private async Task SweepTemplateAsync(IDocumentSession templateSession, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var stream = await templateSession.Events.FetchForExclusiveWriting<RecurringExpense>(id, ct);
        var template = stream.Aggregate;
        if (template is null) return;

        var group = await templateSession.LoadAsync<ExpenseGroupDocument>(template.ExpenseGroupId, ct);
        if (group?.DeletedAt is not null)
        {
            logger.LogInformation(
                "Skipping recurring expense {RecurringExpenseId}: group {GroupId} is deleted", id, template.ExpenseGroupId);
            return;
        }

        var due = template.DueOccurrences(now);
        if (due.Count == 0) return;

        foreach (var date in due)
        {
            // An existing stream is a retry, a voided or edited Expense, or an edit landing on a date already created.
            var expenseId = RecurrenceSchedule.OccurrenceId(id, date);
            if (await templateSession.Events.FetchStreamStateAsync(expenseId, ct) is null)
            {
                var expense = template.BuildExpense(date, now, coveredByCreatedEvent: false);
                templateSession.Events.StartStream(expense.Id, expense.DomainEvents.ToArray());
            }
            template.MarkOccurred(date, now);
        }

        stream.AppendMany(template.DomainEvents.ToArray());
        await templateSession.SaveChangesAsync(ct);
    }

    // The exclusive lock holds until the transaction ends, so operations and the sweep serialize per template.
    private async Task<(JasperFx.Events.IEventStream<RecurringExpense> Stream, RecurringExpense Template)> LoadAsync(
        Guid householdId, Guid id, CancellationToken ct)
    {
        var stream = await session.Events.FetchForExclusiveWriting<RecurringExpense>(id, ct);
        if (stream.Aggregate is null || stream.Aggregate.HouseholdId != householdId)
            throw new InvalidOperationException("Recurring expense not found.");
        await EnsureActiveOwnedGroupAsync(householdId, stream.Aggregate.ExpenseGroupId, ct);
        return (stream, stream.Aggregate);
    }

    private async Task EnsureActiveOwnedGroupAsync(Guid householdId, Guid groupId, CancellationToken ct)
    {
        var group = await session.LoadAsync<ExpenseGroupDocument>(groupId, ct);
        if (group is null || group.HouseholdId != householdId || group.DeletedAt is not null)
            throw new InvalidOperationException("Expense group not found.");
    }

    private static List<FundingSource> ToDomain(IReadOnlyList<FundingSourceDto> dtos) =>
        dtos.Select(f => new FundingSource(f.UserId, f.Cents)).ToList();

    private static List<Allocation> ToDomain(IReadOnlyList<AllocationDto> dtos) =>
        dtos.Select(a => new Allocation(a.UserId, a.Cents)).ToList();
}
