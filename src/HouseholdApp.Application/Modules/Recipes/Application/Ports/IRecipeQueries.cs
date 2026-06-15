namespace HouseholdApp.Application.Modules.Recipes.Application.Ports;

public sealed record RecipeSummary(Guid Id, string Title, string? Description, int? Servings, string? SourceUrl);

public sealed record RecipeDetail(
    Guid Id, string Title, string? Description,
    int? Servings, string? SourceUrl, string? Notes,
    IReadOnlyList<IngredientDto> Ingredients,
    IReadOnlyList<InstructionStepDto> Instructions,
    DateTimeOffset CreatedAt);

public interface IRecipeQueries
{
    Task<IReadOnlyList<RecipeSummary>> ListAsync(Guid householdId, CancellationToken ct = default);
    Task<RecipeDetail?> GetAsync(Guid recipeId, CancellationToken ct = default);
}
