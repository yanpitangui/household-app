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

    [Test]
    public async Task Recorded_expense_shows_payer_chip_on_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        // Create an expense group first (required for Record page)
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Expenses/Groups");
        await page.FillAsync("input[name='NewName']", "Food");
        await page.ClickAsync("button:has-text('+ Create')");
        await page.Locator("text=Food").WaitForAsync();

        // Navigate to Record page
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/Expenses/Record");

        // Fill the form — payer is the current user (first/only member)
        await page.FillAsync("input[name='Description']", "Test Groceries");
        await page.FillAsync("input[name='AmountReais']", "100");

        // Select the group
        var groupSelect = page.Locator("select[name='GroupId']");
        await groupSelect.SelectOptionAsync(new SelectOptionValue { Label = "Food" });

        // Submit
        await page.ClickAsync("button[type='submit']:has-text('Record')");
        await page.WaitForURLAsync($"**/h/{householdId}/Expenses");

        // Payer chip must appear in the expense row
        await Assert.That(
            await page.Locator(".payer-chip").First.IsVisibleAsync()
        ).IsTrue();
    }
}
