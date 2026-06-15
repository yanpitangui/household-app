using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Lists;

public class ListsIndexModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IListCommands listCommands,
    IListQueries listQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty, Required]
    public string NewListName { get; set; } = "";

    public IReadOnlyList<ListSummary> Lists { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Lists = await listQueries.ListAsync(HouseholdId);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Lists = await listQueries.ListAsync(HouseholdId);
            return Page();
        }

        var listId = await listCommands.CreateListAsync(HouseholdId, NewListName);
        return RedirectToPage("List", new { householdId = HouseholdId, listId });
    }

    public async Task<IActionResult> OnPostDeleteListAsync(Guid listId)
    {
        await listCommands.DeleteListAsync(listId);
        return RedirectToPage(new { householdId = HouseholdId });
    }
}
