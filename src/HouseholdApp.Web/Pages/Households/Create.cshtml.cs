using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Households;

public class CreateHouseholdModel(ICurrentUser currentUser, IHouseholdCommands householdCommands)
    : AuthenticatedPageModel(currentUser)
{
    [BindProperty, Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var id = await householdCommands.CreateAsync(Name);
        return RedirectToPage("Detail", new { id });
    }
}
