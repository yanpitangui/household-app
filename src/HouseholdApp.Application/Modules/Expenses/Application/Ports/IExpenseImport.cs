using HouseholdApp.Application.Modules.Expenses.Application.Import;

namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public interface IExpenseImport
{
    Task<bool> GroupNameTakenAsync(Guid householdId, string groupName, CancellationToken ct = default);

    /// <summary>
    /// Creates a new Expense Group named <paramref name="groupName"/> and records every planned
    /// expense and settlement in it. <paramref name="memberForColumn"/> maps each person column
    /// of the plan to a household member. Returns the new group's id.
    /// </summary>
    Task<Guid> ImportAsync(
        Guid householdId, string groupName, ImportPlan plan, IReadOnlyList<Guid> memberForColumn,
        CancellationToken ct = default);
}
