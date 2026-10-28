using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Infrastructure;

public static class ExpensesPageHelpers
{
    private const string SseCounterScript = """
        if (!window.__sseTracked) {
            window.__sseTracked = true;
            window.__sseMessages = 0;
            document.addEventListener('htmx:sseMessage', () => window.__sseMessages++);
        }
        """;

    /// <summary>A page that counts SSE swaps on every document it loads, so tests can wait for the stream's first render.</summary>
    public static async Task<IPage> NewTrackedPageAsync(this IBrowserContext ctx)
    {
        var page = await ctx.NewPageAsync();
        await page.AddInitScriptAsync(SseCounterScript);
        return page;
    }

    /// <summary>Zeroes the SSE swap count before a boosted navigation so the next wait targets the new page's stream.</summary>
    public static Task ResetSseCountAsync(this IPage page) =>
        page.EvaluateAsync("() => { window.__sseMessages = 0; }");

    /// <summary>Waits for the stream's initial render since the last load or reset, so the page is subscribed.</summary>
    public static Task WaitForFirstSseRenderAsync(this IPage page) =>
        page.WaitForFunctionAsync("() => window.__sseMessages > 0");

    public static bool IsGroupPage(string url, Guid? groupId = null) =>
        groupId is { } id
            ? url.Contains($"/expenses/groups/{id}", StringComparison.OrdinalIgnoreCase)
            : Regex.IsMatch(url, "/expenses/groups/[0-9a-f-]{36}$", RegexOptions.IgnoreCase);

    /// <summary>Creates a group through the UI and returns its id, landing on the group page.</summary>
    public static async Task<Guid> CreateGroupAsync(this IPage page, string appUrl, Guid householdId, string name)
    {
        await page.GotoAsync($"{appUrl}/h/{householdId}/expenses/groups/new");
        await page.FillAsync("input[name='Form.Name']", name);
        await page.ClickAsync("button:has-text('+ Create')");
        await page.WaitForURLAsync(url => IsGroupPage(url));
        return Guid.Parse(new Uri(page.Url).Segments[^1]);
    }

    /// <summary>Records <paramref name="amount"/> paid by the page's user, split equally, in the group; lands on its page.</summary>
    public static async Task RecordEqualSplitAsync(
        this IPage page, string appUrl, Guid householdId, Guid groupId, string description, string amount)
    {
        await page.GotoAsync($"{appUrl}/h/{householdId}/expenses/record?group={groupId}");
        await page.FillAsync("input[name='Description']", description);
        await page.FillAsync("input[name='Amount']", amount);
        await page.ResetSseCountAsync();
        await page.ClickAsync("button[type='submit']:has-text('Record')");
        await page.WaitForURLAsync(url => IsGroupPage(url, groupId));
        await page.Locator($".expense-row-header:has-text('{description}')").WaitForAsync();
        await page.WaitForFirstSseRenderAsync();
    }
}
