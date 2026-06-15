using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class ExpenseGroupsModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseCommands expenseCommands,
    IExpenseQueries expenseQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty, Required]
    public string NewName { get; set; } = "";

    [BindProperty]
    public string? NewDescription { get; set; }

    public IReadOnlyList<ExpenseGroupSummary> Groups { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Groups = await expenseQueries.ListExpenseGroupsAsync(HouseholdId);
    }

    [RequireManage]
    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            Groups = await expenseQueries.ListExpenseGroupsAsync(HouseholdId);
            return Page();
        }

        await expenseCommands.CreateExpenseGroupAsync(HouseholdId, NewName, NewDescription);
        TempData["Success"] = "Group created.";
        return RedirectToPage(new { householdId = HouseholdId });
    }

    [RequireManage]
    public async Task<IActionResult> OnPostDeleteAsync(Guid groupId)
    {
        try
        {
            await expenseCommands.DeleteExpenseGroupAsync(groupId);
            TempData["Success"] = "Group deleted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage(new { householdId = HouseholdId });
    }
}
