using HouseholdApp.Application.Modules.Recipes.Domain;

namespace HouseholdApp.Application.Modules.Recipes.Application.Ports;

public interface IRecipeRepository
{
    Task SaveAsync(Recipe recipe, CancellationToken ct = default);
    Task DeleteAsync(Guid householdId, Guid recipeId, CancellationToken ct = default);
}
