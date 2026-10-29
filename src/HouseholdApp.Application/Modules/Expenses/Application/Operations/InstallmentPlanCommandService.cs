using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Events;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

/// <summary>
/// Every operation commits the plan events and the Expenses they create or void in one SaveChangesAsync, so the
/// plan and its installments never drift apart.
/// </summary>
public sealed class InstallmentPlanCommandService(
    IDocumentSession session,
    ICurrentUser currentUser,
    TimeProvider time,
    IEventBus eventBus,
    ISessionFactory sessionFactory,
    ILogger<InstallmentPlanCommandService> logger) : IInstallmentPlanCommands
{
    public async Task<Guid> CreateAsync(
        Guid householdId, Guid groupId, string description, Guid payerId, long totalCents, long downPaymentCents,
        int count, DateOnly purchaseDate, DateOnly firstDate, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default)
    {
        await EnsureActiveOwnedGroupAsync(householdId, groupId, ct);
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var plan = InstallmentPlan.Create(
            householdId, groupId, description, payerId, totalCents, downPaymentCents, count, purchaseDate, firstDate,
            ToDomain(allocations), today, now, currentUser.Id);

        var dueNumbers = plan.DueNumbers(today).ToList();
        var installments = dueNumbers.Select(n => plan.BuildInstallment(n, now, coveredByPlanEvent: true)).ToList();
        // Every installment already due: the plan is complete from the start, no sweep needed.
        if (dueNumbers.Count == plan.Terms.Numbers.Count()) plan.Complete(now);

        session.Events.StartStream(plan.Id, plan.DomainEvents.ToArray());
        foreach (var installment in installments)
            session.Events.StartStream(installment.Id, installment.DomainEvents.ToArray());

        await session.SaveChangesAsync(ct);
        return plan.Id;
    }

    public async Task UpdateAsync(
        Guid householdId, Guid planId, string description, Guid payerId, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var (stream, plan) = await LoadPlanAsync(householdId, planId, ct);
        var fromNumber = await NextNumberAsync(plan, ct);
        plan.Update(description, payerId, ToDomain(allocations), fromNumber, now, currentUser.Id);
        stream.AppendMany(plan.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
    }

    public async Task<Guid> PayRemainingEarlyAsync(
        Guid householdId, Guid planId, long payoffCents, string payoffDescription, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var (stream, plan) = await LoadPlanAsync(householdId, planId, ct);
        var fromNumber = await NextNumberAsync(plan, ct);
        var payoff = plan.PayEarly(fromNumber, payoffCents, payoffDescription, today, now, currentUser.Id);
        session.Events.StartStream(payoff.Id, payoff.DomainEvents.ToArray());
        stream.AppendMany(plan.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
        return payoff.Id;
    }

    // Plan operations and the sweep serialize on the plan stream lock, so the cancel sees every installment.
    public async Task CancelAsync(Guid householdId, Guid planId, bool voidCreated, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var (stream, plan) = await LoadPlanAsync(householdId, planId, ct);
        var voided = new List<VoidedInstallment>();
        if (voidCreated)
        {
            var created = await session.Query<ExpenseReadModel>()
                .Where(e => e.InstallmentPlanId == planId && !e.IsVoided)
                .ToListAsync(ct);
            foreach (var model in created.Where(e => e.Installment is { IsPayoff: false }))
            {
                var expenseStream = await session.Events.FetchForWriting<Expense>(model.Id, ct);
                var expense = expenseStream.Aggregate;
                if (expense is null) continue;
                expense.Void("Installment plan cancelled", now, currentUser.Id, coveredByPlanEvent: true);
                expenseStream.AppendMany(expense.DomainEvents.ToArray());
                voided.Add(new VoidedInstallment(
                    expense.Id, model.Installment!.Number, expense.FundingSources, expense.Allocations));
            }
        }

        plan.Cancel(voided.OrderBy(v => v.Number).ToList(), now, currentUser.Id);
        stream.AppendMany(plan.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
    }

    public async Task RunSweepAsync(DateOnly today, CancellationToken ct = default)
    {
        var planIds = await session.Query<InstallmentPlanDocument>()
            .Where(p => p.Status == InstallmentPlanStatus.Active)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);

        // One session per plan, so the plan lock and transaction end with the plan: a skipped, locked or failed plan
        // never holds a lock or leaves an aborted transaction for the plans after it.
        foreach (var planId in planIds)
        {
            try
            {
                await using var planSession = sessionFactory.OpenSession();
                await SweepPlanAsync(planSession, planId, today, ct);
            }
            catch (ExistingStreamIdCollisionException)
            {
                eventBus.Discard();
                logger.LogInformation("Installment plan {PlanId} was swept concurrently", planId);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                eventBus.Discard();
                logger.LogError(ex, "Sweeping installment plan {PlanId} failed", planId);
            }
        }
    }

    private async Task SweepPlanAsync(IDocumentSession planSession, Guid planId, DateOnly today, CancellationToken ct)
    {
        var stream = await planSession.Events.FetchForExclusiveWriting<InstallmentPlan>(planId, ct);
        var plan = stream.Aggregate;
        if (plan is null || plan.Status != InstallmentPlanStatus.Active) return;

        var group = await planSession.LoadAsync<ExpenseGroupDocument>(plan.ExpenseGroupId, ct);
        if (group?.DeletedAt is not null)
        {
            logger.LogInformation(
                "Skipping installment plan {PlanId}: group {GroupId} is deleted", planId, plan.ExpenseGroupId);
            return;
        }

        var existing = await CreatedNumbersAsync(planSession, planId, ct);
        var now = time.GetUtcNow();
        var due = plan.DueNumbers(today).Where(n => !existing.Contains(n)).ToList();
        foreach (var number in due)
        {
            var installment = plan.BuildInstallment(number, now, coveredByPlanEvent: false);
            planSession.Events.StartStream(installment.Id, installment.DomainEvents.ToArray());
        }

        if (existing.Count + due.Count == plan.Terms.Numbers.Count()) plan.Complete(now);
        if (plan.DomainEvents.Count > 0) stream.AppendMany(plan.DomainEvents.ToArray());
        if (due.Count == 0 && plan.DomainEvents.Count == 0) return;

        await planSession.SaveChangesAsync(ct);
    }

    // The exclusive lock holds until the transaction ends, so plan operations and the sweep serialize per plan and
    // each one reads the installments and plan events the previous one committed.
    private async Task<(JasperFx.Events.IEventStream<InstallmentPlan> Stream, InstallmentPlan Plan)> LoadPlanAsync(
        Guid householdId, Guid planId, CancellationToken ct)
    {
        var stream = await session.Events.FetchForExclusiveWriting<InstallmentPlan>(planId, ct);
        if (stream.Aggregate is null || stream.Aggregate.HouseholdId != householdId)
            throw new InvalidOperationException("Installment plan not found.");
        await EnsureActiveOwnedGroupAsync(householdId, stream.Aggregate.ExpenseGroupId, ct);
        return (stream, stream.Aggregate);
    }

    // The number after the highest created one (the plan's first number when none exists), so a payoff or an
    // update never covers a number that already exists.
    private async Task<int> NextNumberAsync(InstallmentPlan plan, CancellationToken ct)
    {
        var existing = await CreatedNumbersAsync(session, plan.Id, ct);
        return existing.Count == 0 ? plan.Terms.FirstNumber : existing.Max() + 1;
    }

    // Installment numbers that have an Expense, voided or not; a voided installment is never created again.
    private async Task<HashSet<int>> CreatedNumbersAsync(
        IQuerySession querySession, Guid planId, CancellationToken ct)
    {
        var links = await querySession.Query<ExpenseReadModel>()
            .Where(e => e.InstallmentPlanId == planId)
            .Where(e => e.Installment != null)
            .Select(e => e.Installment!)
            .ToListAsync(ct);
        return links.Where(l => !l.IsPayoff).Select(l => l.Number).ToHashSet();
    }

    private async Task EnsureActiveOwnedGroupAsync(Guid householdId, Guid groupId, CancellationToken ct)
    {
        var group = await session.LoadAsync<ExpenseGroupDocument>(groupId, ct);
        if (group is null || group.HouseholdId != householdId || group.DeletedAt is not null)
            throw new InvalidOperationException("Expense group not found.");
    }

    private static List<Allocation> ToDomain(IReadOnlyList<AllocationDto> allocations) =>
        allocations.Select(a => new Allocation(a.UserId, a.Cents)).ToList();
}
