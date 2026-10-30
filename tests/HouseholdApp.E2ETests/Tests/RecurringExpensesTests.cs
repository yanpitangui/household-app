using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[Category(Shards.Second)]
public class RecurringExpensesTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Record_creates_a_recurring_expense_and_opens_its_template()
    {
        await using var context = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(context, $"Recurring HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await context.NewTrackedPageAsync();
        var groupId = await page.CreateGroupAsync(pw.AppUrl, householdId, "Home");

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/record?group={groupId}&repeat=recurring");
        await page.FillAsync("input[name='Description']", "Rent");
        await page.FillAsync("input[name='Amount']", "1200");
        await page.SelectOptionAsync("#RecurringFrequency", "Monthly");
        await page.FillAsync("#RecurringFirstDate", DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd"));
        await page.FillAsync("#RecurringTime", "12:00");

        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync("#record-save-btn"),
            new() { UrlFunc = url => url.Contains("/expenses/recurring/", StringComparison.OrdinalIgnoreCase) });

        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Rent");
        await Assert.That(await page.Locator(".plan-card").First.InnerTextAsync()).Contains("1200.00").And.Contains("Monthly");
        await Assert.That(await page.Locator("a:has-text('Edit')").IsVisibleAsync()).IsTrue();

        await page.ClickAsync("button:has-text('End')");
        await page.RunAndWaitForNavigationAsync(
            () => page.Locator("#end-recurring-sheet button[type='submit']").ClickAsync(),
            new() { UrlFunc = url => url.Contains("/expenses/recurring/", StringComparison.OrdinalIgnoreCase) });
        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Ended");
        await Assert.That(await page.Locator("a:has-text('Edit')").CountAsync()).IsEqualTo(0);
    }
}

