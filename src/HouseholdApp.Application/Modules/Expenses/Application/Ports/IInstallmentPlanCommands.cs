namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public interface IInstallmentPlanCommands
{
    /// <summary>Creates the plan and, in the same transaction, every installment already due (catch-up).</summary>
    Task<Guid> CreateAsync(
        Guid householdId, Guid groupId, string description, Guid payerId, long totalCents, long downPaymentCents,
        int count,
        DateOnly purchaseDate, DateOnly firstDate, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default);

    /// <summary>Description, payer and split for the installments not created yet.</summary>
    Task UpdateAsync(
        Guid householdId, Guid planId, string description, Guid payerId, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default);

    /// <summary>Early Payoff; returns the payoff Expense id.</summary>
    Task<Guid> PayRemainingEarlyAsync(
        Guid householdId, Guid planId, long payoffCents, string payoffDescription, CancellationToken ct = default);

    Task CancelAsync(Guid householdId, Guid planId, bool voidCreated, CancellationToken ct = default);

    /// <summary>Creates every due installment that does not exist yet, for every active plan. Safe to rerun.</summary>
    Task RunSweepAsync(DateOnly today, CancellationToken ct = default);
}
