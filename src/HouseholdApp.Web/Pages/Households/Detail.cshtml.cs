using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Notifications.Application;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HouseholdApp.Web.Pages.Households;

public class HouseholdDetailModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IHouseholdQueries householdQueries,
    IHouseholdQueriesWithLastModified householdQueriesWithLastModified,
    IHouseholdCommands householdCommands,
    IOptions<PushOptions> pushOptions) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public override Guid HouseholdId
    {
        get => Id;
        set => Id = value;
    }

    public HouseholdDetail? Household { get; private set; }
    public string? InviteToken { get; private set; }
    public string VapidPublicKey => pushOptions.Value.VapidPublicKey;

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await householdQueriesWithLastModified.GetWithLastModifiedAsync(Id);
        Household = result.Value;

        if (Household is not null)
            Response.Cookies.Append("last_household", Id.ToString(),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(90), IsEssential = true });
        else if (Request.Cookies["last_household"] == Id.ToString())
            Response.Cookies.Delete("last_household");

        return Household is null ? Page() : this.NotModifiedOr304(result.LastModified) ?? Page();
    }

    [RequireManage]
    public async Task<IActionResult> OnPostInviteAsync()
    {
        InviteToken = await householdCommands.InviteAsync(Id);
        Household = await householdQueries.GetAsync(Id);
        return Page();
    }

    [RequireManage]
    public async Task<IActionResult> OnPostRemoveMemberAsync(Guid targetUserId)
    {
        await householdCommands.RemoveMemberAsync(Id, targetUserId);
        TempData["Success"] = Loc["Flash.MemberRemoved"].Value;
        return RedirectToPage(new { id = Id });
    }
}
