using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Households;

public class HouseholdDetailModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IHouseholdQueries householdQueries,
    IHouseholdCommands householdCommands) : HouseholdPageModel(currentUser, guard)
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

    public async Task OnGetAsync()
    {
        Household = await householdQueries.GetAsync(Id);
        if (Household is not null)
            Response.Cookies.Append("last_household", Id.ToString(),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(90), IsEssential = true });
        else if (Request.Cookies["last_household"] == Id.ToString())
            Response.Cookies.Delete("last_household");
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
