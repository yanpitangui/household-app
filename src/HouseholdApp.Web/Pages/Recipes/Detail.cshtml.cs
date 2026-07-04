using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Recipes;

public class RecipeDetailModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IRecipeQueriesWithETag recipeQueriesWithETag,
    IRecipeCommands recipeCommands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid RecipeId { get; set; }

    public RecipeDetail? Recipe { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await recipeQueriesWithETag.GetWithETagAsync(HouseholdId, RecipeId);
        Recipe = result.Value;
        if (Recipe is null) return NotFound();
        return this.NotModifiedOr304(result.ETag) ?? Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        await recipeCommands.DeleteRecipeAsync(HouseholdId, RecipeId);
        TempData["Success"] = Loc["Flash.RecipeDeleted"].Value;
        return RedirectToPage("Index", new { householdId = HouseholdId });
    }
}
