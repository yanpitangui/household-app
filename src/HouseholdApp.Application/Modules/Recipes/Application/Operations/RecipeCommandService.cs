using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Recipes.Application.Operations;

public sealed class RecipeCommandService(
    IRecipeRepository repo,
    IUnitOfWork uow,
    IEventBus eventBus,
    TimeProvider time,
    ICurrentUser currentUser) : IRecipeCommands
{
    public async Task<Guid> CreateRecipeAsync(
        Guid householdId, string title, string? description,
        int? servings, string? sourceUrl, string? notes,
        IReadOnlyList<IngredientDto> ingredients,
        IReadOnlyList<InstructionStepDto> instructions,
        CancellationToken ct = default)
    {
        var recipe = Recipe.Create(
            householdId, title, description, servings, sourceUrl, notes,
            ingredients.Select(i => new Ingredient(i.Name, i.Quantity, i.Unit)).ToList(),
            instructions.Select(s => new InstructionStep(s.Order, s.Text)).ToList(),
            currentUser.Id, time.GetUtcNow());

        await uow.BeginTransactionAsync(ct);
        await repo.SaveAsync(recipe, ct);
        eventBus.EnqueueAll(recipe);
        await uow.CommitAsync(ct);
        return recipe.Id;
    }

    public async Task DeleteRecipeAsync(Guid recipeId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        await repo.DeleteAsync(recipeId, ct);
        await uow.CommitAsync(ct);
    }
}
