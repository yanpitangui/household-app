using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public class SelectRegressionTest(PlaywrightFixture pw)
{
    private async Task<bool> IsNativeSelectVisible(IPage page)
    {
        return await page.EvaluateAsync<bool>("""
            () => {
                const sel = document.querySelector('select.form-select');
                if (!sel) return false;
                const hasNativeClass = sel.classList.contains('app-select-native');
                const rect = sel.getBoundingClientRect();
                const visible = rect.width > 4 || rect.height > 4;
                console.log('[select-check] enhanced=' + (sel.dataset.appSelectEnhanced) +
                    ' hasNativeClass=' + hasNativeClass +
                    ' rect=' + JSON.stringify({w: Math.round(rect.width), h: Math.round(rect.height)}));
                return visible && !hasNativeClass;
            }
        """);
    }

    [Test]
    public async Task AssignTo_select_stays_hidden_after_task_create()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        ctx.Console += (_, e) => Console.WriteLine($"[browser] {e.Type}: {e.Text}");
        var householdId = await pw.CreateHouseholdAsync(ctx, $"SelectTest HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Check on initial load
        var visibleInitial = await IsNativeSelectVisible(page);
        Console.WriteLine($"[test] Native select visible on initial load: {visibleInitial}");
        await Assert.That(visibleInitial).IsFalse();

        // Create a task (simulates the form submit + hx-boost swap)
        await page.FillAsync("input[name='NewTitle']", "Test task");
        await page.ClickAsync("button[type='submit']:has-text('Add Task')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(300); // let JS settle

        var visibleAfterCreate = await IsNativeSelectVisible(page);
        Console.WriteLine($"[test] Native select visible after task create: {visibleAfterCreate}");
        await Assert.That(visibleAfterCreate).IsFalse();

        // Create another task to trigger another swap
        await page.FillAsync("input[name='NewTitle']", "Test task 2");
        await page.ClickAsync("button[type='submit']:has-text('Add Task')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(300);

        var visibleAfterSecond = await IsNativeSelectVisible(page);
        Console.WriteLine($"[test] Native select visible after second create: {visibleAfterSecond}");
        await Assert.That(visibleAfterSecond).IsFalse();
    }

    [Test]
    public async Task AssignTo_select_stays_hidden_after_complete()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        ctx.Console += (_, e) => Console.WriteLine($"[browser] {e.Type}: {e.Text}");
        var householdId = await pw.CreateHouseholdAsync(ctx, $"SelectTest2 HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/tasks");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Create a task first
        await page.FillAsync("input[name='NewTitle']", "Task to complete");
        await page.ClickAsync("button[type='submit']:has-text('Add Task')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(300);

        // Click the check-box (complete button)
        await page.ClickAsync("button.check-box");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(300);

        var visible = await IsNativeSelectVisible(page);
        Console.WriteLine($"[test] Native select visible after complete: {visible}");
        await Assert.That(visible).IsFalse();
    }
}
