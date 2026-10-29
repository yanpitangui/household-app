using HouseholdApp.E2ETests.Infrastructure;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
public class RecurringTasksTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Unreadable_cron_shows_a_field_error_and_a_valid_one_creates_the_recurring_task()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Recurring HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks/recurring");

        var title = $"Water plants {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewTitle']", title);
        await page.FillAsync("input[name='NewCronExpression']", "every monday");
        await page.ClickAsync("form button[type='submit']");
        await page.Locator("text=Use a 5-field cron").WaitForAsync();

        await page.FillAsync("input[name='NewTitle']", title);
        await page.FillAsync("input[name='NewCronExpression']", "0 9 * * 1");
        await page.ClickAsync("form button[type='submit']");
        await page.Locator($".list-row-name:has-text('{title}')").WaitForAsync();
    }
}
