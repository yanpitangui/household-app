using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class NewExpenseGroupModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseCommands expenseCommands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty, Required]
    public string NewName { get; set; } = "";

    [BindProperty]
    public string? NewDescription { get; set; }

    public void OnGet() { }

    [RequireManage]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var groupId = await expenseCommands.CreateExpenseGroupAsync(HouseholdId, NewName, NewDescription);
        TempData["Success"] = Loc["Flash.GroupCreated"].Value;
        return RedirectToPage("Group", new { householdId = HouseholdId, groupId });
    }
}
