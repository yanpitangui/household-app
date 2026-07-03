using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class ActivityModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IActivityFeedQueries activityFeedQueries) : HouseholdPageModel(currentUser, guard)
{
    private const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    public IReadOnlyList<ActivityFeedItem> Items { get; private set; } = [];
    public string? NextCursor { get; private set; }

    public async Task OnGetAsync()
    {
        var page = await activityFeedQueries.GetActivityFeedAsync(HouseholdId, CurrentUserId, null, PageSize);
        Items = page.Items;
        NextCursor = page.NextCursor;
    }

    public async Task<IActionResult> OnGetLoadMoreAsync(string cursor)
    {
        var page = await activityFeedQueries.GetActivityFeedAsync(HouseholdId, CurrentUserId, cursor, PageSize);
        return Partial("_ActivityRows", page);
    }
}
