using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public class RecipesTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Create_recipe_appears_in_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Recipe HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/recipes/create");

        var recipeTitle = $"Pasta {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='Title']", recipeTitle);
        await page.ClickAsync("button[type='submit']");
        // hx-boost redirects to Recipes/Index; wait for Index heading instead of URL change
        await page.Locator(".page-heading:has-text('Recipes')").WaitForAsync();

        await Assert.That(await page.Locator($"text={recipeTitle}").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task View_recipe_shows_ingredients_and_instructions()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Recipe HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/recipes/create");

        var recipeTitle = $"View pasta {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='Title']", recipeTitle);
        await page.FillAsync("textarea[name='InstructionsText']", "Mix everything together.");
        await page.ClickAsync("button[type='submit']");
        await page.Locator(".page-heading:has-text('Recipes')").WaitForAsync();

        // Navigate to detail — wait for heading containing the recipe title (not the index "Recipes" heading)
        await page.ClickAsync($"text={recipeTitle}");
        await page.Locator($".page-heading:has-text('{recipeTitle}')").WaitForAsync();

        await Assert.That(await page.Locator("text=Mix everything together.").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Add_recipe_ingredients_to_new_shopping_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Recipe HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        var recipeTitle = $"AddToList {Guid.NewGuid().ToString("N")[..8]}";
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/recipes/create");
        await page.FillAsync("input[name='Title']", recipeTitle);
        await page.FillAsync("#ingredient-add-input", "eggs");
        await page.ClickAsync("input[name='Title']"); // blur commits ingredient
        await page.ClickAsync("button[type='submit']");
        await page.Locator(".page-heading:has-text('Recipes')").WaitForAsync();

        await page.ClickAsync($"text={recipeTitle}");
        await page.Locator($".page-heading:has-text('{recipeTitle}')").WaitForAsync();

        await page.ClickAsync("a:has-text('🛒')");
        await page.Locator(".page-heading:has-text('Add to')").WaitForAsync();

        var listName = $"My List {Guid.NewGuid().ToString("N")[..6]}";
        await page.FillAsync("input[name='NewListName']", listName);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/lists/**");

        await Assert.That(await page.Locator(".check-item-text", new() { HasText = "eggs" }).IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Delete_recipe_removes_it()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Recipe HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        var recipeTitle = $"Delete pasta {Guid.NewGuid().ToString("N")[..8]}";
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/recipes/create");
        await page.FillAsync("input[name='Title']", recipeTitle);
        await page.ClickAsync("button[type='submit']");
        await page.Locator(".page-heading:has-text('Recipes')").WaitForAsync();
        await Assert.That(await page.Locator($"text={recipeTitle}").IsVisibleAsync()).IsTrue();

        // Navigate to detail page then delete
        await page.ClickAsync($"text={recipeTitle}");
        await page.Locator(".page-heading").WaitForAsync();

        // Last-Modified has 1-second resolution; wait so the post-delete cache
        // regeneration stamps a value distinct from the create-time stamp,
        // otherwise the Index GET gets a false 304 and shows the stale list.
        await page.WaitForTimeoutAsync(1000);

        await page.ClickAsync("button:has-text('Delete')");
        await page.ClickAsync("#confirm-ok");
        await page.Locator(".page-heading:has-text('Recipes')").WaitForAsync();
        await Assert.That(await page.Locator($"text={recipeTitle}").IsVisibleAsync()).IsFalse();
    }
}
