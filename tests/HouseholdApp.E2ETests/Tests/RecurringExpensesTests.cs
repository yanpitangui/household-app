using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[Category(Shards.Second)]
public class RecurringExpensesTests(PlaywrightFixture pw)
{
    private static bool IsRecurringPage(string url) => url.Contains("/expenses/recurring/", StringComparison.OrdinalIgnoreCase);

    // First dates are whole days ahead at noon UTC, so the template stays upcoming and untouched by the sweep
    // whatever time of day the test runs.
    private static async Task<Guid> RecordRecurringAsync(
        IPage page, string appUrl, Guid householdId, Guid groupId, string description, string amount, int daysAhead)
    {
        await page.GotoAsync($"{appUrl}/h/{householdId}/expenses/record?group={groupId}&repeat=recurring");
        await page.FillAsync("input[name='Description']", description);
        await page.FillAsync("input[name='Amount']", amount);
        await page.SelectOptionAsync("#RecurringFrequency", "Monthly");
        await page.FillAsync("#RecurringFirstDate", DateTime.UtcNow.AddDays(daysAhead).ToString("yyyy-MM-dd"));
        await page.FillAsync("#RecurringTime", "12:00");

        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync("#record-save-btn"),
            new() { UrlFunc = IsRecurringPage });
        return Guid.Parse(new Uri(page.Url).Segments[^1]);
    }

    private static Task SubmitAsync(IPage page, string selector) =>
        page.RunAndWaitForNavigationAsync(
            () => page.Locator(selector).ClickAsync(),
            new() { UrlFunc = IsRecurringPage });

    private static Task PauseAsync(IPage page) => SubmitAsync(page, "button:has-text('Pause')");

    private static async Task EndAsync(IPage page)
    {
        await page.ClickAsync(".plan-actions button:has-text('End')");
        await SubmitAsync(page, "#end-recurring-sheet button[type='submit']");
    }

    [Test]
    public async Task Record_creates_a_recurring_expense_and_opens_its_template()
    {
        await using var context = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(context, $"Recurring HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await context.NewTrackedPageAsync();
        var groupId = await page.CreateGroupAsync(pw.AppUrl, householdId, "Home");

        await RecordRecurringAsync(page, pw.AppUrl, householdId, groupId, "Rent", "1200", 2);

        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Rent");
        await Assert.That(await page.Locator(".plan-card").First.InnerTextAsync()).Contains("1200.00").And.Contains("Monthly");
        await Assert.That(await page.Locator("a:has-text('Edit')").IsVisibleAsync()).IsTrue();

        await EndAsync(page);
        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Ended");
        await Assert.That(await page.Locator("a:has-text('Edit')").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Pause_and_resume_from_template_page()
    {
        await using var context = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(context, $"Recurring HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await context.NewTrackedPageAsync();
        var groupId = await page.CreateGroupAsync(pw.AppUrl, householdId, "Home");
        var recurringId = await RecordRecurringAsync(page, pw.AppUrl, householdId, groupId, "Rent", "1200", 2);
        var templateUrl = page.Url;

        await PauseAsync(page);

        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Paused");
        await Assert.That(await page.Locator(".plan-card").First.InnerTextAsync()).Contains("nothing is created until you resume");
        await Assert.That(await page.Locator("button:has-text('Pause')").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("button:has-text('Resume')").IsVisibleAsync()).IsTrue();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        await Assert.That(await page.Locator($".upcoming-row[href$='/Recurring/{recurringId}']").CountAsync()).IsEqualTo(0);

        await page.GotoAsync(templateUrl);
        await SubmitAsync(page, "button:has-text('Resume')");

        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Active");
        await Assert.That(await page.Locator(".plan-card").First.InnerTextAsync()).Contains("Next occurrence");
        await Assert.That(await page.Locator("button:has-text('Pause')").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator(".activity-row:has-text('paused')").CountAsync()).IsEqualTo(1);
        await Assert.That(await page.Locator(".activity-row:has-text('resumed')").CountAsync()).IsEqualTo(1);

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        await Assert.That(await page.Locator($".upcoming-row[href$='/Recurring/{recurringId}']").CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Ended_template_offers_no_actions_after_pause()
    {
        await using var context = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(context, $"Recurring HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await context.NewTrackedPageAsync();
        var groupId = await page.CreateGroupAsync(pw.AppUrl, householdId, "Home");
        await RecordRecurringAsync(page, pw.AppUrl, householdId, groupId, "Gym", "90", 2);

        await PauseAsync(page);
        await EndAsync(page);

        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Ended");
        await Assert.That(await page.Locator(".plan-actions").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("a:has-text('Edit')").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("button:has-text('Pause')").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("button:has-text('Resume')").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("#end-recurring-sheet").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Plans_and_recurring_list_shows_plans_and_orders_rows()
    {
        await using var context = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(context, $"Recurring HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await context.NewTrackedPageAsync();
        var groupId = await page.CreateGroupAsync(pw.AppUrl, householdId, "Home");

        var planId = await InstallmentPlansTests.RecordPlanAsync(
            page, pw.AppUrl, householdId, groupId, "New fridge", "1200", 4, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));
        await RecordRecurringAsync(page, pw.AppUrl, householdId, groupId, "Internet", "100", 3);
        await RecordRecurringAsync(page, pw.AppUrl, householdId, groupId, "Rent", "1200", 2);
        await PauseAsync(page);

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync(".expenses-secondary-link:has-text('Plans & recurring')"),
            new() { UrlFunc = url => url.EndsWith("/Expenses/Recurring", StringComparison.OrdinalIgnoreCase) });

        await Assert.That(await page.Locator(".page-heading").InnerTextAsync()).Contains("Plans & recurring");

        var planRow = page.Locator("a.list-row:has-text('New fridge')");
        await Assert.That(await planRow.InnerTextAsync()).Contains("💳");
        await Assert.That(await planRow.GetAttributeAsync("href")).EndsWith($"/Expenses/Plans/{planId}", StringComparison.OrdinalIgnoreCase);

        // Active rows by next date (Internet in 3 days, fridge in 5), then the paused Rent.
        var names = await page.Locator("a.list-row .list-row-name").AllInnerTextsAsync();
        await Assert.That(string.Join(" | ", names.Select(n => n.Trim()))).IsEqualTo("Internet | New fridge | Rent");
        await Assert.That(await page.Locator("a.list-row:has-text('Rent')").InnerTextAsync()).Contains("Paused");
    }
}
