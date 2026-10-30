using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class RecurringExpensesModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseQueries expenseQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    public IReadOnlyList<RecurringExpenseSummary> RecurringExpenses { get; private set; } = [];
    public Dictionary<Guid, string> GroupNames { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var recurringTask = expenseQueries.ListRecurringExpensesAsync(HouseholdId);
        var groupsTask = expenseQueries.ListExpenseGroupsAsync(HouseholdId);
        await Task.WhenAll(recurringTask, groupsTask);
        RecurringExpenses = recurringTask.Result;
        GroupNames = groupsTask.Result.ToDictionary(g => g.Id, g => g.Name);
    }
}
