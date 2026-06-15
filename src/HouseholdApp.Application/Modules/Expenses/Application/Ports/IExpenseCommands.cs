namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public sealed record FundingSourceDto(Guid UserId, long Cents);
public sealed record AllocationDto(Guid UserId, long Cents);

public interface IExpenseCommands
{
    Task<Guid> RecordExpenseAsync(
        Guid householdId, Guid expenseGroupId, string description, DateTimeOffset date,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default);

    Task VoidExpenseAsync(Guid expenseId, string? reason, CancellationToken ct = default);

    Task<Guid> RecordSettlementAsync(
        Guid householdId, Guid payerId, Guid recipientId, long cents, DateTimeOffset date,
        CancellationToken ct = default);

    Task<Guid> CreateExpenseGroupAsync(
        Guid householdId, string name, string? description,
        CancellationToken ct = default);

    Task<Guid> CreateRecurringExpenseAsync(
        Guid householdId, Guid expenseGroupId, string description,
        string cronExpression,
        IReadOnlyList<FundingSourceDto> defaultFundingSources,
        IReadOnlyList<AllocationDto> defaultAllocations,
        CancellationToken ct = default);

    Task DeactivateRecurringExpenseAsync(Guid recurringExpenseId, CancellationToken ct = default);

    Task SpawnRecurringExpenseAsync(Guid recurringExpenseId, CancellationToken ct = default);

    Task DeleteExpenseGroupAsync(Guid groupId, CancellationToken ct = default);
}
