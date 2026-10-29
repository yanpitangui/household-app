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

    public ExpensesHome Home { get; private set; } = new([], [], [], []);
    public IReadOnlyList<ExpenseTimelineEntry> Entries { get; private set; } = [];
    public UpcomingSummary Upcoming { get; private set; } = UpcomingSummary.Empty;

    public async Task OnGetAsync()
    {
        Home = await expenseQueries.GetExpensesHomeAsync(HouseholdId, CurrentUserId);
        Entries = ExpenseTimeline.Merge(Home.Expenses, Home.Settlements);
        Upcoming = await expenseQueries.GetUpcomingAsync(HouseholdId, null, CurrentUserId);
    }

    // Older rows load on demand and do not live-update; the SSE stream keeps rendering Recent only.
    public async Task<IActionResult> OnGetOlderAsync(string? cursor)
    {
        var page = await expenseQueries.GetOlderTimelineAsync(HouseholdId, null, cursor);
        var list = new ExpensesListViewModel(
            HouseholdId, null, CurrentUserId, ExpenseTimeline.Merge(page.Expenses, page.Settlements));
        return Partial("_TimelinePage", new TimelinePageViewModel(list, page.NextCursor));
    }

    public async Task<IActionResult> OnPostVoidAsync(Guid expenseId)
    {
        await expenseCommands.VoidExpenseAsync(HouseholdId, expenseId, null);
        TempData["Success"] = Loc["Flash.ExpenseVoided"].Value;
        return RedirectToPage(new { householdId = HouseholdId });
    }

    // Target of the Undo button in the flash shown after a group is deleted.
    [RequireManage]
    public async Task<IActionResult> OnPostRestoreGroupAsync(Guid groupId)
    {
        try
        {
            await expenseCommands.RestoreExpenseGroupAsync(HouseholdId, groupId);
        }
        catch (InvalidOperationException)
        {
            TempData["Error"] = Loc["Expenses.Group.NotFound"].Value;
            return RedirectToPage(new { householdId = HouseholdId });
        }

        TempData["Success"] = Loc["Flash.GroupRestored"].Value;
        return RedirectToPage(new { householdId = HouseholdId });
    }
}
