using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public interface IRecurringExpenseRepository
{
    Task<RecurringExpense?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(RecurringExpense expense, CancellationToken ct = default);

    /// <summary>Cron of every active template, by template id.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetActiveSchedulesAsync(CancellationToken ct = default);
}
