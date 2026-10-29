using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

public enum InstallmentPlanStatus { Active, Completed, PaidEarly, Cancelled }

/// <summary>
/// A purchase split into monthly installments (parcelas). Event-sourced: each operation builds its event, applies it
/// and raises it, so replaying the stream (Create + Apply) rebuilds the same state.
/// </summary>
public sealed class InstallmentPlan : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ExpenseGroupId { get; private set; }
    public string Description { get; private set; } = default!;
    public Guid PayerId { get; private set; }
    public long TotalCents { get; private set; }
    public int Count { get; private set; }
    public DateOnly PurchaseDate { get; private set; }
    public DateOnly FirstDate { get; private set; }
    public IReadOnlyList<SplitSegment> Segments { get; private set; } = [];
    public InstallmentPlanStatus Status { get; private set; }

    private InstallmentPlan() { }

    public static InstallmentPlan Create(
        Guid householdId, Guid expenseGroupId, string description, Guid payerId,
        long totalCents, int count, DateOnly purchaseDate, DateOnly firstDate,
        IReadOnlyList<Allocation> allocations, DateOnly today, DateTimeOffset now, Guid performedByUserId,
        Guid? id = null)
    {
        if (count is < InstallmentSchedule.MinCount or > InstallmentSchedule.MaxCount)
            throw new InvalidOperationException(
                $"Installment count must be between {InstallmentSchedule.MinCount} and {InstallmentSchedule.MaxCount}.");
        if (totalCents < count)
            throw new InvalidOperationException("Each installment must be at least one cent.");
        ValidateSegment(totalCents, count, new SplitSegment(1, allocations));

        var planId = id ?? Guid.CreateVersion7();
        var catchUp = Enumerable.Range(1, count).Count(n => InstallmentSchedule.DueDate(firstDate, n) <= today);
        var created = new InstallmentPlanCreated(
            Guid.CreateVersion7(), now, planId, householdId, expenseGroupId, description, payerId,
            totalCents, count, purchaseDate, firstDate, allocations, catchUp, performedByUserId);

        var plan = Create(created);
        plan.Raise(created);
        return plan;
    }

    public IEnumerable<int> DueNumbers(DateOnly today) =>
        Enumerable.Range(1, Count).Where(n => InstallmentSchedule.DueDate(FirstDate, n) <= today);

    public Expense BuildInstallment(int number, DateTimeOffset now, bool coveredByPlanEvent)
    {
        var due = InstallmentSchedule.DueDate(FirstDate, number);
        return Expense.Record(
            HouseholdId, ExpenseGroupId, Description,
            new DateTimeOffset(due, TimeOnly.MinValue, TimeSpan.Zero),
            [new FundingSource(PayerId, InstallmentSchedule.Amount(TotalCents, Count, number))],
            InstallmentSchedule.Allocations(TotalCents, Count, Segments, number),
            now, PayerId,
            id: InstallmentSchedule.InstallmentExpenseId(Id, number),
            installment: new InstallmentLink(Id, number, Count),
            coveredByPlanEvent: coveredByPlanEvent);
    }

    /// <summary>Description, payer and split for installments <paramref name="fromNumber"/>..Count (not yet created).</summary>
    public void Update(
        string description, Guid payerId, IReadOnlyList<Allocation> allocations, int fromNumber,
        DateTimeOffset now, Guid performedByUserId)
    {
        EnsureActive();
        if (fromNumber < 1) throw new InvalidOperationException("Installment numbers start at 1.");
        if (fromNumber > Count) throw new InvalidOperationException("The plan has no installments left to change.");
        var segment = new SplitSegment(fromNumber, allocations);
        ValidateSegment(TotalCents, Count, segment);

        var updated = new InstallmentPlanUpdated(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, description, payerId, segment,
            Description, performedByUserId);
        Apply(updated);
        Raise(updated);
    }

    /// <summary>Early Payoff: one Expense dated today for installments fromNumber..Count; the plan ends.</summary>
    public Expense PayEarly(
        int fromNumber, long payoffCents, string payoffDescription, DateOnly today, DateTimeOffset now,
        Guid performedByUserId)
    {
        EnsureActive();
        if (fromNumber < 1) throw new InvalidOperationException("Installment numbers start at 1.");
        if (fromNumber > Count) throw new InvalidOperationException("The plan has no installments left to pay.");
        var remaining = InstallmentSchedule.AmountRange(TotalCents, Count, fromNumber, Count);
        if (payoffCents <= 0 || payoffCents > remaining)
            throw new InvalidOperationException("The payoff must be more than zero and at most the remaining amount.");

        var allocations = InstallmentSchedule.Distribute(payoffCents, RemainingShares(fromNumber))
            .Where(a => a.Cents > 0).ToList();
        var expense = Expense.Record(
            HouseholdId, ExpenseGroupId, payoffDescription,
            new DateTimeOffset(today, TimeOnly.MinValue, TimeSpan.Zero),
            [new FundingSource(PayerId, payoffCents)], allocations, now, performedByUserId,
            installment: new InstallmentLink(Id, fromNumber, Count, ThroughNumber: Count),
            coveredByPlanEvent: true);

        var paid = new InstallmentPlanPaidEarly(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, Description, expense.Id,
            fromNumber, Count, remaining, payoffCents, PayerId, allocations, performedByUserId);
        Apply(paid);
        Raise(paid);
        return expense;
    }

    public void Cancel(IReadOnlyList<VoidedInstallment> voided, DateTimeOffset now, Guid performedByUserId)
    {
        EnsureActive();
        var cancelled = new InstallmentPlanCancelled(
            Guid.CreateVersion7(), now, Id, HouseholdId, ExpenseGroupId, Description, voided, performedByUserId);
        Apply(cancelled);
        Raise(cancelled);
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != InstallmentPlanStatus.Active) return;
        var completed = new InstallmentPlanCompleted(Guid.CreateVersion7(), now, Id, HouseholdId);
        Apply(completed);
        Raise(completed);
    }

    public static InstallmentPlan Create(InstallmentPlanCreated e) => new()
    {
        Id = e.PlanId,
        HouseholdId = e.HouseholdId,
        ExpenseGroupId = e.ExpenseGroupId,
        Description = e.Description,
        PayerId = e.PayerId,
        TotalCents = e.TotalCents,
        Count = e.Count,
        PurchaseDate = e.PurchaseDate,
        FirstDate = e.FirstDate,
        Segments = [new SplitSegment(1, e.Allocations)],
        Status = InstallmentPlanStatus.Active
    };

    public void Apply(InstallmentPlanUpdated e)
    {
        Description = e.Description;
        PayerId = e.PayerId;
        Segments = Segments.Where(s => s.FromNumber < e.Segment.FromNumber).Append(e.Segment).ToList();
    }

    public void Apply(InstallmentPlanPaidEarly e) => Status = InstallmentPlanStatus.PaidEarly;
    public void Apply(InstallmentPlanCancelled e) => Status = InstallmentPlanStatus.Cancelled;
    public void Apply(InstallmentPlanCompleted e) => Status = InstallmentPlanStatus.Completed;

    private IReadOnlyList<Allocation> RemainingShares(int fromNumber) =>
        InstallmentSchedule.RemainingShares(TotalCents, Count, Segments, fromNumber);

    private void EnsureActive()
    {
        if (Status != InstallmentPlanStatus.Active)
            throw new InvalidOperationException("This installment plan has ended.");
    }

    private static void ValidateSegment(long totalCents, int count, SplitSegment segment)
    {
        var allocations = segment.Allocations;
        if (allocations.Count == 0 || allocations.Any(a => a.Cents < 0))
            throw new InvalidOperationException("Split the installments between at least one member.");
        if (allocations.Select(a => a.UserId).Distinct().Count() != allocations.Count)
            throw new InvalidOperationException("Each member can appear only once in the split.");
        var expected = InstallmentSchedule.AmountRange(totalCents, count, segment.FromNumber, count);
        if (allocations.Sum(a => a.Cents) != expected)
            throw new InvalidOperationException(
                $"Sum of allocations ({allocations.Sum(a => a.Cents)}) must equal the installments it covers ({expected}).");
        if (!InstallmentSchedule.EveryShareCoversACent(totalCents, count, segment))
            throw new InvalidOperationException("Each member's share must be at least one cent per installment.");
    }
}
