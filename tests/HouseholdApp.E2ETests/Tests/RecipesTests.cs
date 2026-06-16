using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
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

        await page.ClickAsync("button:has-text('Delete')");
        await page.ClickAsync("#confirm-ok");
        await page.Locator($"text={recipeTitle}").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await Assert.That(await page.Locator($"text={recipeTitle}").IsVisibleAsync()).IsFalse();
    }
}
