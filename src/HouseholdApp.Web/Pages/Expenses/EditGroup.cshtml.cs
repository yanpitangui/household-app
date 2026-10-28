using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class EditExpenseGroupModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseCommands expenseCommands,
    IExpenseQueries expenseQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid GroupId { get; set; }

    [BindProperty]
    public GroupFormInput Form { get; set; } = new();

    [RequireManage]
    public async Task<IActionResult> OnGetAsync()
    {
        // Active groups only: a deleted group can't be edited.
        var group = (await expenseQueries.ListExpenseGroupsAsync(HouseholdId)).FirstOrDefault(g => g.Id == GroupId);
        if (group is null) return NotFound();

        Form = GroupFormInput.From(group);
        return Page();
    }

    [RequireManage]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!GroupIcons.IsValid(Form.ResolvedIcon))
            ModelState.AddModelError("Form.IconOther", Loc["Expenses.GroupIcon.Invalid"].Value);
        if (!ModelState.IsValid) return Page();

        try
        {
            await expenseCommands.UpdateExpenseGroupAsync(
                HouseholdId, GroupId, Form.Name, Form.Description, Form.ResolvedIcon);
        }
        catch (InvalidOperationException)
        {
            // The group was deleted while the form was open.
            TempData["Error"] = Loc["Expenses.Group.Gone"].Value;
            return RedirectToPage("Index", new { householdId = HouseholdId });
        }

        TempData["Success"] = Loc["Flash.GroupUpdated"].Value;
        return RedirectToPage("Group", new { householdId = HouseholdId, groupId = GroupId });
    }
}
