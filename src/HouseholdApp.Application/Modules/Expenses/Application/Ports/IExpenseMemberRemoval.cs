namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public enum RecurringRemovalEffect { Redistributed, Ended }

public sealed record RecurringRemovalImpact(Guid RecurringExpenseId, string Description, RecurringRemovalEffect Effect);

/// <summary>NetCents: positive when the household owes the member, negative when the member owes the household.</summary>
public sealed record ExpenseRemovalImpact(
    long NetCents,
    IReadOnlyList<RecurringRemovalImpact> Recurring,
    IReadOnlyList<string> InstallmentPlans);

public interface IExpenseMemberRemoval
{
    Task<ExpenseRemovalImpact> PreviewAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task ApplyAsync(Guid householdId, Guid userId, CancellationToken ct = default);
}
