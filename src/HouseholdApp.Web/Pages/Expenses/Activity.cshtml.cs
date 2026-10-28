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
    IExpenseCommands expenseCommands) : HouseholdPageModel(currentUser, guard)
{
    private const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    public IReadOnlyList<ActivityFeedItem> Items { get; private set; } = [];
    public string? NextCursor { get; private set; }

    public async Task OnGetAsync()
    {
        var page = await activityFeedQueries.GetActivityFeedAsync(HouseholdId, CurrentUserId, null, PageSize);
        Items = await ForViewerAsync(page.Items);
        NextCursor = page.NextCursor;
    }

    public async Task<IActionResult> OnGetLoadMoreAsync(string cursor)
    {
        var page = await activityFeedQueries.GetActivityFeedAsync(HouseholdId, CurrentUserId, cursor, PageSize);
        return Partial("_ActivityRows", page with { Items = await ForViewerAsync(page.Items) });
    }

    [RequireManage]
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

    // Restore needs manage rights, so members without them get rows without the button.
    private async Task<IReadOnlyList<ActivityFeedItem>> ForViewerAsync(IReadOnlyList<ActivityFeedItem> items) =>
        await Guard.CanManageAsync(HouseholdId, CurrentUserId)
            ? items
            : items.Select(i => i with { CanRestore = false }).ToList();
}
