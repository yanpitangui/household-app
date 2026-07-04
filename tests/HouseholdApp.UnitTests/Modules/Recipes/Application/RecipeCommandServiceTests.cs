using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Recipes.Application;

public sealed class RecipeCommandServiceTests
{
    private readonly IRecipeRepository _repo = Substitute.For<IRecipeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly RecipeCommandService _sut;

    public RecipeCommandServiceTests()
    {
        _currentUser.Id.Returns(Guid.NewGuid());
        _sut = new RecipeCommandService(_repo, _uow, _eventBus, new FakeTimeProvider(), _currentUser);
    }

    [Test]
    public async Task CreateRecipeAsync_saves_recipe_and_returns_id()
    {
        var ingredients = new List<IngredientDto> { new("Flour", "2", "cups") };
        var instructions = new List<InstructionStepDto> { new(1, "Mix flour with water") };

        var id = await _sut.CreateRecipeAsync(
            Guid.NewGuid(), "Bread", null, 4, null, null,
            ingredients, instructions);

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task CreateRecipeAsync_maps_ingredients_to_recipe()
    {
        Recipe? captured = null;
        await _repo.SaveAsync(Arg.Do<Recipe>(r => captured = r), Arg.Any<CancellationToken>());

        var ingredients = new List<IngredientDto>
        {
            new("Sugar", "1", "tbsp"),
            new("Salt", "1", "tsp")
        };
        var instructions = new List<InstructionStepDto> { new(1, "Mix") };

        await _sut.CreateRecipeAsync(
            Guid.NewGuid(), "Cake", null, null, null, null,
            ingredients, instructions);

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Ingredients.Count).IsEqualTo(2);
        await Assert.That(captured.Ingredients[0].Name).IsEqualTo("Sugar");
    }

    [Test]
    public async Task CreateRecipeAsync_maps_instructions_to_recipe()
    {
        Recipe? captured = null;
        await _repo.SaveAsync(Arg.Do<Recipe>(r => captured = r), Arg.Any<CancellationToken>());

        var ingredients = new List<IngredientDto> { new("Water", "1", "L") };
        var instructions = new List<InstructionStepDto>
        {
            new(1, "Boil water"),
            new(2, "Add pasta")
        };

        await _sut.CreateRecipeAsync(
            Guid.NewGuid(), "Pasta", null, null, null, null,
            ingredients, instructions);

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Instructions.Count).IsEqualTo(2);
        await Assert.That(captured.Instructions[1].Text).IsEqualTo("Add pasta");
    }

    [Test]
    public async Task DeleteRecipeAsync_calls_repo_delete()
    {
        var householdId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();

        await _sut.DeleteRecipeAsync(householdId, recipeId);

        await _repo.Received(1).DeleteAsync(recipeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteRecipeAsync_enqueues_RecipeDeleted_event()
    {
        var householdId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();

        await _sut.DeleteRecipeAsync(householdId, recipeId);

        _eventBus.Received(1).Enqueue(Arg.Is<RecipeDeleted>(e =>
            e.RecipeId == recipeId && e.HouseholdId == householdId));
    }
}
