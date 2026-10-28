using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class ExpenseGroupModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseQueries expenseQueries,
    IExpenseCommands expenseCommands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid GroupId { get; set; }

    public GroupPage Group { get; private set; } = default!;
    public bool CanManage { get; private set; }
    public IReadOnlyList<ExpenseTimelineEntry> Entries { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var page = await expenseQueries.GetGroupPageAsync(HouseholdId, GroupId, CurrentUserId);
        if (page is null) return NotFound();

        Group = page;
        CanManage = await Guard.CanManageAsync(HouseholdId, CurrentUserId);
        Entries = ExpenseTimeline.Merge(page.Expenses, page.Settlements);
        return Page();
    }

    public async Task<IActionResult> OnGetOlderAsync(string? cursor)
    {
        var page = await expenseQueries.GetOlderTimelineAsync(HouseholdId, GroupId, cursor);
        var list = new ExpensesListViewModel(
            HouseholdId, GroupId, CurrentUserId, ExpenseTimeline.Merge(page.Expenses, page.Settlements));
        return Partial("_TimelinePage", new TimelinePageViewModel(list, page.NextCursor));
    }

    public async Task<IActionResult> OnPostVoidAsync(Guid expenseId)
    {
        await expenseCommands.VoidExpenseAsync(HouseholdId, expenseId, null);
        TempData["Success"] = Loc["Flash.ExpenseVoided"].Value;
        return RedirectToPage(new { householdId = HouseholdId, groupId = GroupId });
    }

    [RequireManage]
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        // Active groups only: one that is already deleted (or never existed) has nothing left to delete.
        var group = (await expenseQueries.ListExpenseGroupsAsync(HouseholdId)).FirstOrDefault(g => g.Id == GroupId);
        if (group is null) return RedirectToPage("Index", new { householdId = HouseholdId });

        await expenseCommands.DeleteExpenseGroupAsync(HouseholdId, GroupId);
        TempData["Success"] = Loc["Flash.GroupDeletedNamed", group.Name].Value;
        TempData["UndoGroupId"] = GroupId.ToString();
        return RedirectToPage("Index", new { householdId = HouseholdId });
    }
}
