using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Lists;

public class ListModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IListCommands listCommands,
    IListQueries listQueries,
    ICatalogQueries catalogQueries,
    ICatalogCommands catalogCommands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid ListId { get; set; }

    public static readonly string[] CategoryEmojis =
    [
        "🍅","🥩","🍞","🥛","❄️","🌾","🥤","🧂","🍫","🪴",
        "🧴","🐾","🍱","🍴","🍿","🔋","🧹","💊","📦","🧺",
        "🥗","🍳","🥬","🧅","🍋","🍇","🥦","🧀","🥚","🫙"
    ];

    public ListDetail? List { get; private set; }
    public IReadOnlyList<CategoryDto> Categories { get; private set; } = [];

    private string CurrentLanguage =>
        HttpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.Name ?? "en";

    public async Task OnGetAsync()
    {
        List = await listQueries.GetAsync(ListId);
        Categories = await catalogQueries.GetCategoriesAsync(HouseholdId, CurrentLanguage);
    }

    public async Task<IActionResult> OnGetSuggestAsync(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName) || itemName.Length < 2)
            return Partial("_ItemSuggestions", Array.Empty<CatalogItemSuggestion>());
        var suggestions = await catalogQueries.SuggestAsync(HouseholdId, itemName, CurrentLanguage);
        return Partial("_ItemSuggestions", suggestions);
    }

    public async Task<IActionResult> OnPostAddItemAsync(Guid listId, string itemName, Guid? catalogItemId, Guid? categoryId)
    {
        await listCommands.AddItemAsync(listId, itemName, catalogItemId, categoryId);
        var updated = await listQueries.GetAsync(listId);
        return Partial("_ItemsList", updated);
    }

    public async Task<IActionResult> OnPostAddCategoryAsync(string categoryName, string categoryEmoji)
    {
        var emoji = string.IsNullOrWhiteSpace(categoryEmoji) ? "🏷️" : categoryEmoji;
        await catalogCommands.AddHouseholdCategoryAsync(HouseholdId, categoryName, emoji);
        var categories = await catalogQueries.GetCategoriesAsync(HouseholdId, CurrentLanguage);
        return Partial("_CategoryOptions", categories);
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
