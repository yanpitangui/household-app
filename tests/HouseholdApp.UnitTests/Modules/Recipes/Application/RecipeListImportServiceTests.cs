using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Recipes.Application;

public sealed class RecipeListImportServiceTests
{
    private readonly IRecipeQueries _recipeQueries = Substitute.For<IRecipeQueries>();
    private readonly ICatalogQueries _catalogQueries = Substitute.For<ICatalogQueries>();
    private readonly RecipeListImportService _sut;

    public RecipeListImportServiceTests()
    {
        _sut = new RecipeListImportService(_recipeQueries, _catalogQueries);
    }

    [Test]
    public async Task ProposeListItemsAsync_returns_empty_when_recipe_not_found()
    {
        _recipeQueries.GetAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RecipeDetail?)null);

        var result = await _sut.ProposeListItemsAsync(Guid.NewGuid(), Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ProposeListItemsAsync_returns_matched_item_with_catalog_data()
    {
        var recipeId = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        var catalogItemId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var recipe = new RecipeDetail(recipeId, "Caldo Verde", null, null, null, null,
            [new IngredientDto("batatas", "4", "médias")], [], DateTimeOffset.UtcNow);
        _recipeQueries.GetAsync(householdId, recipeId, Arg.Any<CancellationToken>()).Returns(recipe);

        var suggestion = new CatalogItemSuggestion(catalogItemId, "Batata", categoryId, "Legumes", "🥬", null);
        _catalogQueries.MatchIngredientsAsync(householdId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CatalogItemSuggestion> { ["batatas"] = suggestion });

        var result = await _sut.ProposeListItemsAsync(householdId, recipeId);

        await Assert.That(result.Count).IsEqualTo(1);
        var item = result[0];
        await Assert.That(item.Name).IsEqualTo("Batata"); // matched → canonical catalog name
        await Assert.That(item.Quantity).IsEqualTo("4");
        await Assert.That(item.Unit).IsEqualTo("médias");
        await Assert.That(item.Matched).IsTrue();
        await Assert.That(item.CatalogItemId).IsEqualTo(catalogItemId);
        await Assert.That(item.CategoryId).IsEqualTo(categoryId);
        await Assert.That(item.CategoryName).IsEqualTo("Legumes");
        await Assert.That(item.CategoryEmoji).IsEqualTo("🥬");
    }

    [Test]
    public async Task ProposeListItemsAsync_returns_unmatched_item_when_no_catalog_match()
    {
        var recipeId = Guid.NewGuid();
        var householdId = Guid.NewGuid();

        var recipe = new RecipeDetail(recipeId, "Salad", null, null, null, null,
            [new IngredientDto("exotic herb", null, null)], [], DateTimeOffset.UtcNow);
        _recipeQueries.GetAsync(householdId, recipeId, Arg.Any<CancellationToken>()).Returns(recipe);

        _catalogQueries.MatchIngredientsAsync(householdId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CatalogItemSuggestion>());

        var result = await _sut.ProposeListItemsAsync(householdId, recipeId);

        await Assert.That(result.Count).IsEqualTo(1);
        var item = result[0];
        await Assert.That(item.Name).IsEqualTo("exotic herb");
        await Assert.That(item.Matched).IsFalse();
        await Assert.That(item.CatalogItemId).IsNull();
        await Assert.That(item.CategoryId).IsNull();
    }

    [Test]
    public async Task ProposeListItemsAsync_handles_mix_of_matched_and_unmatched()
    {
        var recipeId = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        var catalogItemId = Guid.NewGuid();

        var recipe = new RecipeDetail(recipeId, "Mixed", null, null, null, null,
            [new IngredientDto("eggs", "3", null), new IngredientDto("saffron", null, null)],
            [], DateTimeOffset.UtcNow);
        _recipeQueries.GetAsync(householdId, recipeId, Arg.Any<CancellationToken>()).Returns(recipe);

        var suggestion = new CatalogItemSuggestion(catalogItemId, "Eggs", null, null, null, null);
        _catalogQueries.MatchIngredientsAsync(householdId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CatalogItemSuggestion> { ["eggs"] = suggestion });

        var result = await _sut.ProposeListItemsAsync(householdId, recipeId);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Matched).IsTrue();
        await Assert.That(result[0].Quantity).IsEqualTo("3");
        await Assert.That(result[1].Name).IsEqualTo("saffron");
        await Assert.That(result[1].Matched).IsFalse();
    }
}
