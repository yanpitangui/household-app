using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Recipes;

public class AddToListModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IRecipeQueries recipeQueries,
    IRecipeListImport recipeListImport,
    IListQueries listQueries,
    IListCommands listCommands,
    ICatalogQueries catalogQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid RecipeId { get; set; }

    public RecipeDetail? Recipe { get; private set; }
    public IReadOnlyList<ProposedListItem> ProposedItems { get; private set; } = [];
    public IReadOnlyList<ListSummary> Lists { get; private set; } = [];
    public IReadOnlyList<CategoryDto> Categories { get; private set; } = [];

    [BindProperty]
    public Guid? TargetListId { get; set; }

    [BindProperty]
    public string? NewListName { get; set; }

    [BindProperty]
    public List<ReviewItem> Items { get; set; } = [];

    public sealed class ReviewItem
    {
        public bool Include { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public string? Quantity { get; set; }
        public string? Unit { get; set; }
        public Guid? CatalogItemId { get; set; }
        public Guid? CategoryId { get; set; }
    }

    private string CurrentLanguage =>
        HttpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.Name ?? "en";

    public async Task<IActionResult> OnGetAsync()
    {
        Recipe = await recipeQueries.GetAsync(RecipeId);
        if (Recipe is null) return NotFound();

        ProposedItems = await recipeListImport.ProposeListItemsAsync(HouseholdId, RecipeId);
        Lists = await listQueries.ListAsync(HouseholdId);
        Categories = await catalogQueries.GetCategoriesAsync(HouseholdId, CurrentLanguage);

        Items = ProposedItems.Select(p => new ReviewItem
        {
            Include = true,
            Name = p.Name,
            Quantity = p.Quantity,
            Unit = p.Unit,
            CatalogItemId = p.CatalogItemId,
            CategoryId = p.CategoryId,
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Guid listId;
        if (TargetListId.HasValue)
        {
            listId = TargetListId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(NewListName))
        {
            listId = await listCommands.CreateListAsync(HouseholdId, NewListName.Trim());
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Select a list or enter a new list name.");
            await ReloadPageDataAsync();
            return Page();
        }

        var bulkItems = Items
            .Where(i => i.Include && !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => new BulkAddItem(i.Name.Trim(), i.Quantity, i.Unit, i.CatalogItemId, i.CategoryId))
            .ToList();

        if (bulkItems.Count > 0)
            await listCommands.BulkAddItemsAsync(listId, bulkItems);

        return RedirectToPage("/Lists/List", new { householdId = HouseholdId, listId });
    }

    private async Task ReloadPageDataAsync()
    {
        Recipe = await recipeQueries.GetAsync(RecipeId);
        ProposedItems = await recipeListImport.ProposeListItemsAsync(HouseholdId, RecipeId);
        Lists = await listQueries.ListAsync(HouseholdId);
        Categories = await catalogQueries.GetCategoriesAsync(HouseholdId, CurrentLanguage);
    }
}
