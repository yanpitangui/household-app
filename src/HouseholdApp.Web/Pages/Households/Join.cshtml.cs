using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Households;

public class JoinHouseholdModel(ICurrentUser currentUser, IHouseholdCommands householdCommands)
    : AuthenticatedPageModel(currentUser)
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public bool AlreadyJoined { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            TempData["Error"] = "Invalid token.";
            return Page();
        }

        var accepted = await householdCommands.AcceptInvitationAsync(Token);

        if (!accepted)
        {
            TempData["Error"] = "Invitation is no longer valid.";
            return Page();
        }

        AlreadyJoined = true;
        return Page();
    }
}
