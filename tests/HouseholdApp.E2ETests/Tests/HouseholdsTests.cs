using HouseholdApp.E2ETests.Infrastructure;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerTestSession)]
public class HouseholdsTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Login_redirects_to_households_index()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/Households/Index");

        await Assert.That(page.Url).Contains("/Households/Index");
        await Assert.That(await page.TitleAsync()).Contains("Household");
    }

    [Test]
    public async Task Root_without_cookie_redirects_to_households_index()
    {
        // fresh context has no last_household cookie
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var page = await ctx.NewPageAsync();

        // GotoAsync follows all HTTP redirects; check final URL
        await page.GotoAsync($"{pw.AppUrl}/");

        await Assert.That(page.Url).Contains("/Households");
    }

    [Test]
    public async Task Root_with_last_household_cookie_redirects_to_that_household()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var uid = Guid.NewGuid().ToString("N")[..8];
        var id = await pw.CreateHouseholdAsync(ctx, $"Cookie Test {uid}");

        // visit detail page to set the last_household cookie
        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{pw.AppUrl}/h/{id}");
        await page.WaitForURLAsync($"{pw.AppUrl}/h/{id}");

        await page.GotoAsync($"{pw.AppUrl}/");
        await page.WaitForURLAsync($"{pw.AppUrl}/h/{id}");

        await Assert.That(page.Url).Contains($"/h/{id}");
    }

    [Test]
    public async Task Create_household_appears_in_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var uid = Guid.NewGuid().ToString("N")[..8];
        var name = $"Test House {uid}";
        await pw.CreateHouseholdAsync(ctx, name);

        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{pw.AppUrl}/Households/Index");

        await Assert.That(await page.Locator($"text={name}").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Household_detail_shows_module_links()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var uid = Guid.NewGuid().ToString("N")[..8];
        var id = await pw.CreateHouseholdAsync(ctx, $"Detail Test {uid}");

        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{pw.AppUrl}/h/{id}");

        await Assert.That(await page.Locator("a.feature-card:has-text('Tasks')").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("a.feature-card:has-text('Lists')").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("a.feature-card:has-text('Expenses')").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("a.feature-card:has-text('Recipes')").IsVisibleAsync()).IsTrue();
    }
}
