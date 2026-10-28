using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
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

    [BindProperty]
    public GroupFormInput Form { get; set; } = new();

    [RequireManage]
    public void OnGet() { }

    [RequireManage]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!GroupIcons.IsValid(Form.ResolvedIcon))
            ModelState.AddModelError("Form.IconOther", Loc["Expenses.GroupIcon.Invalid"].Value);
        if (!ModelState.IsValid) return Page();

        var groupId = await expenseCommands.CreateExpenseGroupAsync(
            HouseholdId, Form.Name, Form.Description, Form.ResolvedIcon);
        TempData["Success"] = Loc["Flash.GroupCreated"].Value;
        return RedirectToPage("Group", new { householdId = HouseholdId, groupId });
    }
}
