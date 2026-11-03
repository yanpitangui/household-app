using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class ActivityModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IActivityFeedQueries activityFeedQueries,
    IExpenseQueries expenseQueries,
    IExpenseCommands expenseCommands) : HouseholdPageModel(currentUser, guard)
{
    private const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    public ActivityListViewModel List { get; private set; } = new([], null, null);

    public async Task OnGetAsync()
    {
        var page = await activityFeedQueries.GetActivityFeedAsync(HouseholdId, CurrentUserId, null, PageSize);
        List = new ActivityListViewModel(await ForViewerAsync(page.Items), page.NextCursor, null);
    }

    public async Task<IActionResult> OnGetLoadMoreAsync(string cursor, string? month)
    {
        var page = await activityFeedQueries.GetActivityFeedAsync(HouseholdId, CurrentUserId, cursor, PageSize);
        return Partial("_ActivityList", new ActivityListViewModel(await ForViewerAsync(page.Items), page.NextCursor, month));
    }

    // Loaded into the expense sheet dialog from the activity page and the plan and recurring histories.
    public async Task<IActionResult> OnGetExpenseAsync(Guid expenseId)
    {
        var sheet = await expenseQueries.GetExpenseSheetAsync(HouseholdId, expenseId);
        return Partial("_ExpenseSheet", new ExpenseSheetViewModel(HouseholdId, sheet, ReturnPath()));
    }

    public async Task<IActionResult> OnPostVoidAsync(Guid expenseId, string? returnUrl)
    {
        await expenseCommands.VoidExpenseAsync(HouseholdId, expenseId, null);
        TempData["Success"] = Loc["Flash.ExpenseVoided"].Value;
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage(new { householdId = HouseholdId });
    }

    [RequirePermission(HouseholdPermission.Manage)]
    public async Task<IActionResult> OnPostRestoreAsync(Guid groupId)
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

    // The page the sheet was opened from, so voiding returns there; htmx sends it as HX-Current-URL.
    private string ReturnPath() =>
        Uri.TryCreate(Request.Headers["HX-Current-URL"].ToString(), UriKind.Absolute, out var current)
            ? current.PathAndQuery
            : Url.Page("/Expenses/Activity", new { householdId = HouseholdId })!;

    // Restore needs manage rights, so members without them get rows without the button.
    private async Task<IReadOnlyList<ActivityFeedItem>> ForViewerAsync(IReadOnlyList<ActivityFeedItem> items) =>
        await Guard.CanManageAsync(HouseholdId, CurrentUserId)
            ? items
            : items.Select(i => i with { CanRestore = false }).ToList();
}
