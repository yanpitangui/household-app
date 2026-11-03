using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Households;

/// <summary>Serves two routes: removing someone (a user id in the route) and leaving yourself (no user id).</summary>
public class RemoveMemberModel(
    ICurrentUser currentUser, IHouseholdGuard guard, IMemberRemovalPreview previews, IHouseholdCommands commands)
    : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? UserId { get; set; }
    public override Guid HouseholdId { get => Id; set => Id = value; }

    public bool IsLeaving => UserId is null;
    public MemberRemovalPreview Preview { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (IsLeaving)
        {
            await commands.LeaveAsync(Id);
            TempData["Success"] = Loc["Flash.LeftHousehold"].Value;
            if (Request.Cookies["last_household"] == Id.ToString()) Response.Cookies.Delete("last_household");
            return RedirectToPage("/Households/Index");
        }
        await commands.RemoveMemberAsync(Id, UserId!.Value);
        TempData["Success"] = Loc["Flash.MemberRemoved"].Value;
        return RedirectToPage("Settings", new { id = Id });
    }

    private async Task<bool> LoadAsync()
    {
        var target = UserId ?? CurrentUserId;
        if (await previews.GetAsync(Id, target) is not { } preview) return false;
        var allowed = IsLeaving
            ? await Guard.HasAsync(Id, CurrentUserId, HouseholdPermission.Leave)
            : await Guard.CanRemoveMemberAsync(Id, CurrentUserId, target, preview.Member.Role);
        if (!allowed) return false;
        Preview = preview;
        return true;
    }
}
