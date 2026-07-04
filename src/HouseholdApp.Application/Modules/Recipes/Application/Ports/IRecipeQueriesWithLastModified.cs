using HouseholdApp.Application.Shared.Caching;

namespace HouseholdApp.Application.Modules.Recipes.Application.Ports;

public interface IRecipeQueriesWithLastModified
{
    Task<WithLastModified<RecipeDetail?>> GetWithLastModifiedAsync(Guid householdId, Guid recipeId, CancellationToken ct = default);
    Task<WithLastModified<IReadOnlyList<RecipeSummary>>> ListWithLastModifiedAsync(Guid householdId, CancellationToken ct = default);
}
