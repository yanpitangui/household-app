using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

public sealed record ExpensesListViewModel(
    Guid HouseholdId,
    Guid CurrentUserId,
    IReadOnlyList<ExpenseListItem> Expenses);
