using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Recipes.Domain;

public sealed record Ingredient(string Name, string? Quantity, string? Unit);
public sealed record InstructionStep(int Order, string Text);

public sealed class Recipe : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public int? Servings { get; private set; }
    public string? SourceUrl { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlyList<Ingredient> Ingredients { get; private set; } = [];
    public IReadOnlyList<InstructionStep> Instructions { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    private Recipe() { }

    public static Recipe Create(
        Guid householdId, string title, string? description,
        int? servings, string? sourceUrl, string? notes,
        IReadOnlyList<Ingredient> ingredients,
        IReadOnlyList<InstructionStep> instructions,
        Guid createdBy, DateTimeOffset now)
    {
        var recipe = new Recipe
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            Title = title,
            Description = description,
            Servings = servings,
            SourceUrl = sourceUrl,
            Notes = notes,
            Ingredients = ingredients,
            Instructions = instructions,
            CreatedBy = createdBy,
            CreatedAt = now
        };
        recipe.Raise(new RecipeCreated(Guid.CreateVersion7(), now, recipe.Id, householdId, createdBy, sourceUrl));
        return recipe;
    }
}
