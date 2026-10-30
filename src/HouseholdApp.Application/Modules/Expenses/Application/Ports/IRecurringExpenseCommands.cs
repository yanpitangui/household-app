using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public interface IRecurringExpenseCommands
{
    /// <summary>
    /// With <paramref name="catchUp"/> and a first date before today, every occurrence up to now is recorded with the
    /// template, in one transaction. Without it, a past first date only sets the rhythm.
    /// </summary>
    Task<Guid> CreateAsync(
        Guid householdId, Guid groupId, string description, RecurrenceSchedule schedule,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations, bool catchUp,
        CancellationToken ct = default);

    Task UpdateAsync(
        Guid householdId, Guid recurringExpenseId, string description, RecurrenceSchedule schedule,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default);

    Task EndAsync(Guid householdId, Guid recurringExpenseId, CancellationToken ct = default);

    /// <summary>Records every occurrence that is due, for every active template.</summary>
    Task RunSweepAsync(CancellationToken ct = default);
}
