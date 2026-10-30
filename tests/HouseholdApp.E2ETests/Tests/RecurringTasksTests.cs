using HouseholdApp.E2ETests.Infrastructure;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
public class RecurringTasksTests(PlaywrightFixture pw)
{
    [Test]
    public async Task A_past_first_date_shows_a_field_error_and_a_valid_schedule_creates_the_recurring_task()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Recurring HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks/recurring");

        var title = $"Water plants {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewTitle']", title);
        await page.SelectOptionAsync("select[name='NewFrequency']", "Weekly");
        await page.FillAsync("input[name='NewFirstDate']", "2020-01-06");
        await page.FillAsync("input[name='NewTime']", "09:00");
        await page.ClickAsync("form button[type='submit']");
        await page.Locator("text=The first date must be today or later").WaitForAsync();

        await page.FillAsync("input[name='NewTitle']", title);
        await page.FillAsync("input[name='NewFirstDate']", DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"));
        await page.ClickAsync("form button[type='submit']");
        await page.Locator($".list-row-name:has-text('{title}')").WaitForAsync();
        await page.Locator(".list-row-meta:has-text('Weekly on')").WaitForAsync();
    }
}
