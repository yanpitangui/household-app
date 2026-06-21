namespace HouseholdApp.Application.Modules.Recipes.Application.Ports;

public sealed record RecipeImportResult(
    bool Success,
    string? ErrorMessage = null,
    string? Title = null,
    string? Description = null,
    string? Servings = null,
    string? SourceUrl = null,
    IReadOnlyList<string>? Ingredients = null,
    string? Instructions = null);

public interface IRecipeImporter
{
    Task<RecipeImportResult> ImportAsync(string url, CancellationToken ct = default);
}
