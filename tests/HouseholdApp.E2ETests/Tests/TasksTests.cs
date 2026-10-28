using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
public class TasksTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Create_task_appears_in_list()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Tasks HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks");

        var taskTitle = $"Buy milk {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewTitle']", taskTitle);
        await page.ClickAsync("button:has-text('+ Add Task')");

        // hx-boost handles the POST→redirect in-place; wait for the task to appear
        await page.Locator($"text={taskTitle}").WaitForAsync();
        await Assert.That(await page.Locator($"text={taskTitle}").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Complete_task_marks_it_done()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Tasks HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks");

        var taskTitle = $"Clean kitchen {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewTitle']", taskTitle);
        await page.ClickAsync("button:has-text('+ Add Task')");
        await page.Locator("button.check-box").WaitForAsync();

        await page.ClickAsync("button.check-box");
        // Wait for the task to leave the pending list
        await page.Locator($"text={taskTitle}").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await Assert.That(await page.Locator($"text={taskTitle}").IsVisibleAsync()).IsFalse();

        // Verify it shows in "All" view
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks?showCompleted=true");
        await page.Locator($"text={taskTitle}").WaitForAsync();
        await Assert.That(await page.Locator($"text={taskTitle}").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Delete_task_removes_it()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Tasks HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks");

        var taskTitle = $"Delete me {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewTitle']", taskTitle);
        await page.ClickAsync("button:has-text('+ Add Task')");
        // Wait for delete button to appear (proves task is in the list)
        await page.Locator("button:has-text('✕')").WaitForAsync();

        await page.ClickAsync("button:has-text('✕')");
        await page.ClickAsync("#confirm-ok");
        await page.Locator($"text={taskTitle}").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await Assert.That(await page.Locator($"text={taskTitle}").IsVisibleAsync()).IsFalse();
    }

    [Test]
    public async Task Recurring_tasks_page_loads()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"RecTask HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks/recurring");

        await Assert.That(await page.Locator(".page-heading:has-text('Recurring Tasks')").IsVisibleAsync()).IsTrue();
    }
}
