using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Lists;

public class ListModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IListCommands listCommands,
    IListQueries listQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid ListId { get; set; }

    public ListDetail? List { get; private set; }

    public async Task OnGetAsync()
    {
        List = await listQueries.GetAsync(ListId);
    }

    public async Task<IActionResult> OnPostAddItemAsync(Guid listId, string itemName, string? category)
    {
        await listCommands.AddItemAsync(listId, itemName, category);
        var updated = await listQueries.GetAsync(listId);
        return Partial("_ItemsList", updated);
    }

    public async Task<IActionResult> OnPostCompleteItemAsync(Guid listId, Guid itemId)
    {
        await listCommands.CompleteItemAsync(listId, itemId);
        var updated = await listQueries.GetAsync(listId);
        return Partial("_ItemsList", updated);
    }

    public async Task<IActionResult> OnPostUncompleteItemAsync(Guid listId, Guid itemId)
    {
        await listCommands.UncompleteItemAsync(listId, itemId);
        var updated = await listQueries.GetAsync(listId);
        return Partial("_ItemsList", updated);
    }

    public async Task<IActionResult> OnPostRemoveItemAsync(Guid listId, Guid itemId)
    {
        await listCommands.RemoveItemAsync(listId, itemId);
        var updated = await listQueries.GetAsync(listId);
        return Partial("_ItemsList", updated);
    }
}
