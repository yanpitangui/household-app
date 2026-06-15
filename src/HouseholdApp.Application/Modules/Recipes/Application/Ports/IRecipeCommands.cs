namespace HouseholdApp.Application.Modules.Recipes.Application.Ports;

public sealed record IngredientDto(string Name, string? Quantity, string? Unit);
public sealed record InstructionStepDto(int Order, string Text);

public interface IRecipeCommands
{
    Task<Guid> CreateRecipeAsync(
        Guid householdId, string title, string? description,
        int? servings, string? sourceUrl, string? notes,
        IReadOnlyList<IngredientDto> ingredients,
        IReadOnlyList<InstructionStepDto> instructions,
        CancellationToken ct = default);

    Task DeleteRecipeAsync(Guid recipeId, CancellationToken ct = default);
}
