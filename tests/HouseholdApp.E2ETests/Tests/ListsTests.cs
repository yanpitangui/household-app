using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerTestSession)]
public class ListsTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Create_list_appears_in_index()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Lists");

        var listName = $"Weekly groceries {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewListName']", listName);
        await page.ClickAsync("button:has-text('Create')");

        await page.WaitForURLAsync("**/Lists/**");
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Lists");

        await Assert.That(await page.Locator($"text={listName}").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Delete_list_removes_it()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        var listName = $"Delete list {Guid.NewGuid().ToString("N")[..8]}";
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Lists");
        await page.FillAsync("input[name='NewListName']", listName);
        await page.ClickAsync("button:has-text('Create')");
        await page.WaitForURLAsync("**/Lists/**");

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Lists");
        await Assert.That(await page.Locator($"text={listName}").IsVisibleAsync()).IsTrue();

        page.Dialog += (_, e) => e.AcceptAsync();
        await page.ClickAsync("button:has-text('Delete')");
        await page.Locator($"text={listName}").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await Assert.That(await page.Locator($"text={listName}").IsVisibleAsync()).IsFalse();
    }
}
