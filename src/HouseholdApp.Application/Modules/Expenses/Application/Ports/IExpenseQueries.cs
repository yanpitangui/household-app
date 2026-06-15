namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public sealed record ExpenseListItem(
    Guid Id, string Description, DateTimeOffset Date, long TotalCents, bool IsVoided);

public sealed record ExpenseDetail(
    Guid Id, Guid ExpenseGroupId, string Description, DateTimeOffset Date,
    long TotalCents, bool IsVoided, string? VoidReason,
    IReadOnlyList<FundingSourceDto> FundingSources,
    IReadOnlyList<AllocationDto> Allocations,
    DateTimeOffset RecordedAt);

public sealed record MemberBalance(Guid UserId, string DisplayName, long Cents);

public sealed record ExpenseGroupSummary(Guid Id, string Name, string? Description);

public sealed record RecurringExpenseSummary(
    Guid Id, string Description, string CronExpression, bool IsActive, Guid ExpenseGroupId);

public interface IExpenseQueries
{
    Task<IReadOnlyList<ExpenseListItem>> ListExpensesAsync(
        Guid householdId, Guid? groupId = null, CancellationToken ct = default);

    Task<ExpenseDetail?> GetExpenseAsync(Guid expenseId, CancellationToken ct = default);

    Task<IReadOnlyList<MemberBalance>> GetHouseholdBalancesAsync(
        Guid householdId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseGroupSummary>> ListExpenseGroupsAsync(
        Guid householdId, CancellationToken ct = default);

    Task<IReadOnlyList<RecurringExpenseSummary>> ListRecurringExpensesAsync(
        Guid householdId, CancellationToken ct = default);
}
