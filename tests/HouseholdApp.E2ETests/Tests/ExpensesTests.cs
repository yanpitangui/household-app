using HouseholdApp.E2ETests.Infrastructure;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerTestSession)]
public class ExpensesTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Expenses_index_loads()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Expenses");

        await Assert.That(await page.Locator(".page-heading:has-text('Expenses')").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("a:has-text('Groups')").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("a:has-text('Recurring')").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Create_expense_group_appears_in_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Expenses/Groups");

        var groupName = $"Groceries {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewName']", groupName);
        await page.ClickAsync("button:has-text('+ Create')");
        // hx-boost redirects back to same Groups URL; wait for element to appear
        await page.Locator($"text={groupName}").WaitForAsync();

        await Assert.That(await page.Locator($"text={groupName}").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Recurring_expenses_page_loads()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Expenses/Recurring");

        await Assert.That(await page.Locator(".page-heading:has-text('Recurring Expenses')").IsVisibleAsync()).IsTrue();
    }
}
