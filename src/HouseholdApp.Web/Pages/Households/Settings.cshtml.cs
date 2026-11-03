using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Households;

public class HouseholdSettingsModel(
    ICurrentUser currentUser, IHouseholdGuard guard,
    IHouseholdQueries queries, IHouseholdCommands commands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public override Guid HouseholdId { get => Id; set => Id = value; }

    [BindProperty, Required, MaxLength(200)] public string Name { get; set; } = "";
    [BindProperty, Required] public string TimeZone { get; set; } = "";

    public HouseholdDetail Household { get; private set; } = default!;
    public HouseholdPermissions Permissions { get; private set; } = HouseholdPermissions.None;
    public IReadOnlyList<MemberRow> Members { get; private set; } = [];
    public IReadOnlyList<PendingInvitation> Invitations { get; private set; } = [];
    private static readonly IReadOnlyList<string> RegionZones =
        TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id).Where(id => id.Contains('/')).Order().ToList();

    // Region zones, plus the household's own zone when a browser reported one outside that list (e.g. "UTC").
    public IReadOnlyList<string> TimeZones { get; private set; } = RegionZones;
    [TempData] public string? InviteToken { get; set; }

    public sealed record MemberRow(HouseholdMemberDto Member, bool IsMe, bool CanRemove, bool CanChangeRole);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        Name = Household.Name;
        TimeZone = Household.TimeZone;
        return Page();
    }

    [RequirePermission(HouseholdPermission.EditSettings)]
    public async Task<IActionResult> OnPostRenameAsync()
    {
        ModelState.Remove(nameof(TimeZone));   // each section's form posts only its own field
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        await commands.RenameAsync(Id, Name);
        return Done("Flash.HouseholdRenamed");
    }

    [RequirePermission(HouseholdPermission.EditSettings)]
    public async Task<IActionResult> OnPostTimeZoneAsync()
    {
        ModelState.Remove(nameof(Name));
        if (!HouseholdTimeZone.IsValid(TimeZone)) ModelState.AddModelError(nameof(TimeZone), Loc["Settings.Household.TimeZoneInvalid"]);
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        await commands.ChangeTimeZoneAsync(Id, TimeZone);
        return Done("Flash.TimeZoneChanged");
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(Guid targetUserId, string role)
    {
        var target = await TargetAsync(targetUserId);
        if (target is null || role is not ("Admin" or "Member")
            || !await Guard.CanChangeRoleAsync(Id, CurrentUserId, target.Role)) return NotFound();
        await commands.ChangeMemberRoleAsync(Id, targetUserId, role);
        return Done("Flash.RoleChanged");
    }

    [RequirePermission(HouseholdPermission.TransferOwnership)]
    public async Task<IActionResult> OnPostTransferAsync(Guid targetUserId)
    {
        if (targetUserId == CurrentUserId || await TargetAsync(targetUserId) is null) return NotFound();
        await commands.TransferOwnershipAsync(Id, targetUserId);
        return Done("Flash.OwnershipTransferred");
    }

    [RequirePermission(HouseholdPermission.Invite)]
    public async Task<IActionResult> OnPostInviteAsync()
    {
        InviteToken = await commands.InviteAsync(Id);
        return RedirectToPage(new { id = Id });   // PRG: a refresh re-shows the same token
    }

    [RequirePermission(HouseholdPermission.Invite)]
    public async Task<IActionResult> OnPostRevokeAsync(Guid invitationId)
    {
        if ((await queries.ListPendingInvitationsAsync(Id)).All(i => i.Id != invitationId)) return NotFound();
        await commands.RevokeInvitationAsync(Id, invitationId);
        return Done("Flash.InvitationRevoked");
    }

    private IActionResult Done(string flashKey)
    {
        TempData["Success"] = Loc[flashKey].Value;
        return RedirectToPage(new { id = Id });
    }

    private async Task<HouseholdMemberDto?> TargetAsync(Guid userId) =>
        (await queries.GetMembersAsync(Id)).FirstOrDefault(m => m.UserId == userId);

    private async Task<bool> LoadAsync()
    {
        var household = await queries.GetAsync(Id);
        if (household is null) return false;
        Household = household;
        if (string.IsNullOrEmpty(Name)) Name = household.Name;
        if (string.IsNullOrEmpty(TimeZone)) TimeZone = household.TimeZone;
        if (!RegionZones.Contains(household.TimeZone)) TimeZones = [household.TimeZone, .. RegionZones];
        Permissions = await Guard.GetPermissionsAsync(Id, CurrentUserId);
        Invitations = Permissions.Can(HouseholdPermission.Invite) ? await queries.ListPendingInvitationsAsync(Id) : [];

        var rows = new List<MemberRow>();
        foreach (var m in household.Members.OrderByDescending(m => m.Role == "Owner").ThenBy(m => m.DisplayName))
        {
            var isMe = m.UserId == CurrentUserId;
            rows.Add(new MemberRow(m, isMe,
                CanRemove: !isMe && await Guard.CanRemoveMemberAsync(Id, CurrentUserId, m.UserId, m.Role),
                CanChangeRole: !isMe && await Guard.CanChangeRoleAsync(Id, CurrentUserId, m.Role)));
        }
        Members = rows;
        return true;
    }
}
