using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Recipes;

public class RecipesIndexModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IRecipeQueriesWithETag recipeQueriesWithETag,
    IRecipeCommands recipeCommands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    public IReadOnlyList<RecipeSummary> Recipes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await recipeQueriesWithETag.ListWithETagAsync(HouseholdId);
        Recipes = result.Value;
        return this.NotModifiedOr304(result.ETag) ?? Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid recipeId)
    {
        await recipeCommands.DeleteRecipeAsync(HouseholdId, recipeId);
        TempData["Success"] = Loc["Flash.RecipeDeleted"].Value;
        return RedirectToPage(new { householdId = HouseholdId });
    }
}
