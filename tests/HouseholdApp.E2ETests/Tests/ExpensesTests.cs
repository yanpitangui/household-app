using HouseholdApp.E2ETests.Infrastructure;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public class ExpensesTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Expenses_index_loads()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");

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

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups");

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

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/recurring");

        await Assert.That(await page.Locator(".page-heading:has-text('Recurring Expenses')").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Recorded_expense_appears_in_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        // Create an expense group first (required for Record page)
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups");
        await page.FillAsync("input[name='NewName']", "Food");
        await page.ClickAsync("button:has-text('+ Create')");
        await page.Locator("text=Food").WaitForAsync();

        // Navigate to Record page
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/record");

        // Fill the form — payer is the current user (first/only member)
        await page.FillAsync("input[name='Description']", "Test Groceries");
        await page.FillAsync("input[name='Amount']", "100");

        // Select the group
        var groupSelect = page.Locator("select[name='GroupId']");
        await groupSelect.SelectOptionAsync(new SelectOptionValue { Label = "Food" });

        // Submit
        await page.ClickAsync("button[type='submit']:has-text('Record')");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");

        // Expense row must appear in the list
        await page.Locator(".expense-row-header").First.WaitForAsync();
        await Assert.That(
            await page.Locator(".expense-row-header:has-text('Test Groceries')").IsVisibleAsync()
        ).IsTrue();
    }

    [Test]
    public async Task Editing_expense_replaces_it_in_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        // Create an expense group first (required for Record page)
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups");
        await page.FillAsync("input[name='NewName']", "Food");
        await page.ClickAsync("button:has-text('+ Create')");
        await page.Locator("text=Food").WaitForAsync();

        // Record the original expense
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/record");
        await page.FillAsync("input[name='Description']", "Test Groceries");
        await page.FillAsync("input[name='Amount']", "100");
        var groupSelect = page.Locator("select[name='GroupId']");
        await groupSelect.SelectOptionAsync(new SelectOptionValue { Label = "Food" });
        await page.ClickAsync("button[type='submit']:has-text('Record')");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");

        // Expand the row to reveal the Edit link, then follow it
        await page.Locator(".expense-row-header:has-text('Test Groceries')").ClickAsync();
        await page.Locator("a:has-text('Edit')").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/expenses/record") && url.Contains("expenseId"));

        // Edit page must be in edit mode with the original values prefilled
        await Assert.That(await page.Locator(".page-heading:has-text('Edit')").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.InputValueAsync("input[name='Description']")).IsEqualTo("Test Groceries");

        // Change description and amount, then save
        await page.FillAsync("input[name='Description']", "Test Groceries Updated");
        await page.FillAsync("input[name='Amount']", "150");
        await page.ClickAsync("button[type='submit']:has-text('Save')");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");

        // Updated entry replaces the original in the list
        await page.Locator(".expense-row-header").First.WaitForAsync();
        await Assert.That(
            await page.Locator(".expense-row-header:has-text('Test Groceries Updated')").IsVisibleAsync()
        ).IsTrue();
        await Assert.That(
            await page.Locator(".expense-row-header:has-text('Test Groceries'):not(:has-text('Updated'))").CountAsync()
        ).IsEqualTo(0);
    }
}
