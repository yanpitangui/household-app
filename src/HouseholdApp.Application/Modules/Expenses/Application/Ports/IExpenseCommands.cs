using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public sealed record FundingSourceDto(Guid UserId, long Cents);
public sealed record AllocationDto(Guid UserId, long Cents);

public interface IExpenseCommands
{
    Task<Guid> RecordExpenseAsync(
        Guid householdId, Guid expenseGroupId, string description, DateTimeOffset date,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default);

    Task VoidExpenseAsync(Guid householdId, Guid expenseId, string? reason, CancellationToken ct = default);

    Task<Guid> EditExpenseAsync(
        Guid householdId, Guid expenseId, string description, DateTimeOffset date,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default);

    Task<Guid> RecordGroupSettlementAsync(
        Guid householdId, Guid groupId, Guid payerId, Guid recipientId, long cents, DateTimeOffset date,
        CancellationToken ct = default);

    /// <summary>
    /// Splits the payment across the pair's groups (see SettlementSplitter) and returns the household settlement
    /// id. Throws <see cref="SettlementSplitException"/> when the amount cannot be split.
    /// </summary>
    Task<Guid> RecordHouseholdSettlementAsync(
        Guid householdId, Guid payerId, Guid recipientId, long cents, DateTimeOffset date,
        CancellationToken ct = default);

    Task<Guid> CreateExpenseGroupAsync(
        Guid householdId, string name, string? description,
        CancellationToken ct = default);

    Task<Guid> CreateRecurringExpenseAsync(
        Guid householdId, Guid expenseGroupId, string description,
        RecurrenceFrequency frequency, DateTimeOffset startAt,
        IReadOnlyList<FundingSourceDto> defaultFundingSources,
        IReadOnlyList<AllocationDto> defaultAllocations,
        CancellationToken ct = default);

    Task UpdateRecurringExpenseAsync(
        Guid recurringExpenseId, string description,
        RecurrenceFrequency frequency, DateTimeOffset startAt,
        IReadOnlyList<FundingSourceDto> defaultFundingSources,
        IReadOnlyList<AllocationDto> defaultAllocations,
        CancellationToken ct = default);

    Task DeactivateRecurringExpenseAsync(Guid recurringExpenseId, CancellationToken ct = default);

    Task SpawnRecurringExpenseAsync(Guid recurringExpenseId, CancellationToken ct = default);

    Task DeleteExpenseGroupAsync(Guid householdId, Guid groupId, CancellationToken ct = default);

    Task RestoreExpenseGroupAsync(Guid householdId, Guid groupId, CancellationToken ct = default);
}
