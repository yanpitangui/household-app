using HouseholdApp.Application.Shared.Caching;

namespace HouseholdApp.Application.Modules.Recipes.Application.Ports;

public interface IRecipeQueriesWithETag
{
    Task<WithETag<RecipeDetail?>> GetWithETagAsync(Guid householdId, Guid recipeId, CancellationToken ct = default);
    Task<WithETag<IReadOnlyList<RecipeSummary>>> ListWithETagAsync(Guid householdId, CancellationToken ct = default);
}
