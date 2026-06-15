using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class ExpensesIndexModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseQueries expenseQueries,
    IExpenseCommands expenseCommands)
    : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    public IReadOnlyList<ExpenseListItem> Expenses { get; private set; } = [];
    public IReadOnlyList<MemberBalance> Balances { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Expenses = await expenseQueries.ListExpensesAsync(HouseholdId);
        Balances = await expenseQueries.GetHouseholdBalancesAsync(HouseholdId);
    }

    public async Task<IActionResult> OnPostVoidAsync(Guid expenseId)
    {
        await expenseCommands.VoidExpenseAsync(expenseId, null);
        TempData["Success"] = "Expense voided.";
        return RedirectToPage(new { householdId = HouseholdId });
    }
}
