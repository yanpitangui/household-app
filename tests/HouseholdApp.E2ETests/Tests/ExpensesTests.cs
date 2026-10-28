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
    public async Task Balance_card_survives_link_navigation()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        // Boosted (htmx) navigation, the path where the card used to be dropped.
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups");
        await page.ClickAsync("a:has-text('Back')");
        await page.Locator(".page-heading:has-text('Expenses')").WaitForAsync();

        await Assert.That(await page.Locator("#expenses-balance-card").CountAsync()).IsEqualTo(1);
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

    [Test]
    public async Task Splitwise_import_preview_checks_the_balance()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/import");
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Splitwise", "casa-ficticia_2026-01-31_export.csv");
        await page.SetInputFilesAsync("input[name='Upload']", fixture);
        await page.Locator("#import-balance-check").WaitForAsync();

        await Assert.That(await page.Locator("#import-summary").InnerTextAsync()).Contains("15 expenses and 3 payments");
        await Assert.That(await page.Locator("#import-balance-check tbody tr").CountAsync()).IsEqualTo(2);
        await Assert.That(await page.Locator("#import-balance-check tbody tr:has-text('❌')").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("input[name='GroupName']").InputValueAsync()).IsEqualTo("casa-ficticia");
        await Assert.That(await page.Locator("#import-errors").InnerTextAsync()).Contains("Choose a household member for each Splitwise person.");
        await Assert.That(await page.Locator("#import-result").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("button:has-text('Import')").IsDisabledAsync()).IsTrue();
    }

    [Test]
    public async Task Decimal_amount_round_trips_under_pt_BR()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        await ctx.AddCookiesAsync([new Cookie
        {
            Name = ".AspNetCore.Culture",
            Value = "c=pt-BR|uic=pt-BR",
            Url = pw.AppUrl,
        }]);
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        // Labels are pt-BR here: "+ Criar", "Registrar", "Editar".
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups");
        await page.FillAsync("input[name='NewName']", "Food");
        await page.ClickAsync("button:has-text('+ Criar')");
        await page.Locator("text=Food").WaitForAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/record");
        await page.FillAsync("input[name='Description']", "Decimal check");
        await page.FillAsync("input[name='Amount']", "12.34");
        await page.Locator("select[name='GroupId']").SelectOptionAsync(new SelectOptionValue { Label = "Food" });
        await page.ClickAsync("button[type='submit']:has-text('Registrar')");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");

        var row = page.Locator(".expense-row-header:has-text('Decimal check')");
        await row.WaitForAsync();
        await Assert.That(await row.InnerTextAsync()).Contains("12,34");

        // Edit form renders the stored amount back into the number input.
        await row.ClickAsync();
        await page.Locator("a:has-text('Editar')").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/expenses/record") && url.Contains("expenseId"));
        await Assert.That(await page.Locator("input[name='Amount']").InputValueAsync()).IsEqualTo("12.34");
    }

    [Test]
    public async Task Settle_page_defaults_without_balances()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/settle");

        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        await Assert.That(await page.Locator("input[name='Date']").InputValueAsync()).IsEqualTo(today);
        await Assert.That(await page.Locator("input[name='Amount']").InputValueAsync()).IsEqualTo("");
    }

    [Test]
    public async Task Balances_read_as_sentences_for_both_members()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await NewTrackedPageAsync(ownerCtx);
        await RecordEqualSplitAsync(ownerPage, householdId, "Shared dinner", "100");

        var ownerRow = ownerPage.Locator("#expenses-balance-card .expenses-person-row");
        await ownerRow.WaitForAsync();
        await Assert.That(await ownerRow.CountAsync()).IsEqualTo(1);
        await Assert.That(await ownerRow.InnerTextAsync()).Contains("Partner owes you").And.Contains("50.00");

        var partnerPage = await NewTrackedPageAsync(partnerCtx);
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        await WaitForFirstSseRenderAsync(partnerPage);
        var partnerRow = partnerPage.Locator("#expenses-balance-card .expenses-person-row");
        await partnerRow.WaitForAsync();
        await Assert.That(await partnerRow.CountAsync()).IsEqualTo(1);
        await Assert.That(await partnerRow.InnerTextAsync()).Contains("You owe Test").And.Contains("50.00");
    }

    [Test]
    public async Task Settle_link_prefills_the_balance_and_records_a_payment()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var page = await NewTrackedPageAsync(ownerCtx);
        await RecordEqualSplitAsync(page, householdId, "Shared dinner", "100");

        await page.Locator("#expenses-balance-card .expenses-settle-with").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/expenses/settle", StringComparison.OrdinalIgnoreCase));

        await Assert.That(await page.Locator("select[name='PayerId'] option:checked").InnerTextAsync()).IsEqualTo("Partner User");
        await Assert.That(await page.Locator("select[name='RecipientId'] option:checked").InnerTextAsync()).IsEqualTo("Test User");
        await Assert.That(await page.Locator("input[name='Amount']").InputValueAsync()).IsEqualTo("50.00");

        await page.ClickAsync("button[type='submit']:has-text('Record Settlement')");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");

        await page.Locator(".payment-row:has-text('Partner paid you')").WaitForAsync();
        await Assert.That(await page.Locator("#expenses-balance-card .expenses-person-row").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Settle_link_survives_the_stream_render()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var page = await NewTrackedPageAsync(ownerCtx);
        await RecordEqualSplitAsync(page, householdId, "Shared dinner", "100");

        // Delays are set so the stream's first render lands while the settle request is in flight.
        await page.RouteAsync(url => url.EndsWith("/stream", StringComparison.OrdinalIgnoreCase), async route =>
        {
            await Task.Delay(300);
            await route.ContinueAsync();
        });
        await page.RouteAsync(url => url.Contains("/expenses/settle", StringComparison.OrdinalIgnoreCase), async route =>
        {
            await Task.Delay(1_000);
            await route.ContinueAsync();
        });
        await page.ReloadAsync();

        await page.Locator("#expenses-balance-card .expenses-settle-with").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/expenses/settle", StringComparison.OrdinalIgnoreCase));
        await Assert.That(await page.Locator("input[name='Amount']").InputValueAsync()).IsEqualTo("50.00");
    }

    [Test]
    public async Task Expanded_row_stays_open_across_a_live_update()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await NewTrackedPageAsync(ownerCtx);
        await RecordEqualSplitAsync(ownerPage, householdId, "Shared dinner", "100");

        var dinnerRow = ownerPage.Locator(".expense-row-wrap:has-text('Shared dinner')");
        await dinnerRow.Locator(".expense-row-header").ClickAsync();
        await Assert.That(await dinnerRow.GetAttributeAsync("class")).Contains("expanded");

        var partnerPage = await partnerCtx.NewPageAsync();
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/record");
        await partnerPage.FillAsync("input[name='Description']", "Partner taxi");
        await partnerPage.FillAsync("input[name='Amount']", "40");
        await partnerPage.Locator("select[name='GroupId']").SelectOptionAsync(new SelectOptionValue { Label = "Food" });
        await partnerPage.ClickAsync("button[type='submit']:has-text('Record')");
        await partnerPage.WaitForURLAsync($"**/h/{householdId}/expenses");

        await ownerPage.Locator(".expense-row-header:has-text('Partner taxi')")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assert.That(await dinnerRow.GetAttributeAsync("class")).Contains("expanded");
    }

    [Test]
    public async Task Payment_appears_live_for_the_other_member()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await NewTrackedPageAsync(ownerCtx);
        await RecordEqualSplitAsync(ownerPage, householdId, "Shared dinner", "100");

        // The first stream render means the partner is subscribed before the payment lands.
        var partnerPage = await NewTrackedPageAsync(partnerCtx);
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        await WaitForFirstSseRenderAsync(partnerPage);
        await Assert.That(await partnerPage.Locator(".payment-row").CountAsync()).IsEqualTo(0);

        await ownerPage.Locator("#expenses-balance-card .expenses-settle-with").ClickAsync();
        await ownerPage.WaitForURLAsync(url => url.Contains("/expenses/settle", StringComparison.OrdinalIgnoreCase));
        await ownerPage.ClickAsync("button[type='submit']:has-text('Record Settlement')");
        await ownerPage.WaitForURLAsync($"**/h/{householdId}/expenses");

        await partnerPage.Locator(".payment-row:has-text('You paid Test')")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assert.That(await partnerPage.Locator("#expenses-balance-card .expenses-person-row").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Expense_appears_live_for_the_other_member()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);

        var partnerPage = await NewTrackedPageAsync(partnerCtx);
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        await WaitForFirstSseRenderAsync(partnerPage);

        var ownerPage = await NewTrackedPageAsync(ownerCtx);
        await RecordEqualSplitAsync(ownerPage, householdId, "Shared dinner", "100");

        await partnerPage.Locator(".expense-row-header:has-text('Shared dinner')")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assert.That(await partnerPage.Locator("#expenses-balance-card .expenses-person-row").InnerTextAsync())
            .Contains("You owe Test");
    }

    /// <summary>Records <paramref name="amount"/> paid by the page's user, split equally across all members, and lands on the Expenses index.</summary>
    private async Task RecordEqualSplitAsync(IPage page, Guid householdId, string description, string amount)
    {
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups");
        await page.FillAsync("input[name='NewName']", "Food");
        await page.ClickAsync("button:has-text('+ Create')");
        await page.Locator("text=Food").WaitForAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/record");
        await page.FillAsync("input[name='Description']", description);
        await page.FillAsync("input[name='Amount']", amount);
        await page.Locator("select[name='GroupId']").SelectOptionAsync(new SelectOptionValue { Label = "Food" });
        await ResetSseCountAsync(page);
        await page.ClickAsync("button[type='submit']:has-text('Record')");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");
        await page.Locator($".expense-row-header:has-text('{description}')").WaitForAsync();
        await WaitForFirstSseRenderAsync(page);
    }

    private const string SseCounterScript = """
        if (!window.__sseTracked) {
            window.__sseTracked = true;
            window.__sseMessages = 0;
            document.addEventListener('htmx:sseMessage', () => window.__sseMessages++);
        }
        """;

    /// <summary>A page that counts SSE swaps on every document it loads, so tests can wait for the stream's first render.</summary>
    private static async Task<IPage> NewTrackedPageAsync(IBrowserContext ctx)
    {
        var page = await ctx.NewPageAsync();
        await page.AddInitScriptAsync(SseCounterScript);
        return page;
    }

    /// <summary>
    /// Zeroes the SSE swap count before a boosted navigation, which keeps the window (and its count)
    /// alive, so <see cref="WaitForFirstSseRenderAsync"/> then waits for the new page's stream.
    /// </summary>
    private static Task ResetSseCountAsync(IPage page) =>
        page.EvaluateAsync("() => { window.__sseMessages = 0; }");

    /// <summary>
    /// Waits until the Expenses stream has swapped in its initial render since the last full load or
    /// <see cref="ResetSseCountAsync"/>, so later clicks hit the settled balance card and the page is
    /// subscribed to live updates.
    /// </summary>
    private static Task WaitForFirstSseRenderAsync(IPage page) =>
        page.WaitForFunctionAsync("() => window.__sseMessages > 0");
}
