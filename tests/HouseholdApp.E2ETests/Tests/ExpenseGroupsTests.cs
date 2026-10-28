using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
public class ExpenseGroupsTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Household_settle_splits_a_partial_payment_across_groups()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);

        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var trip = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        var home = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Home");
        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");     // partner owes 100
        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, home, "Groceries", "60"); // owner owes 30

        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/settle");
        await Assert.That(await partnerPage.Locator("input[name='Amount']").InputValueAsync()).IsEqualTo("70.00");
        await partnerPage.FillAsync("input[name='Amount']", "40");
        await partnerPage.Locator(".settle-breakdown-summary:has-text('30.00')").WaitForAsync();
        await Assert.That(await partnerPage.Locator(".settle-breakdown-row").CountAsync()).IsEqualTo(2);

        await partnerPage.ClickAsync("button[type='submit']:has-text('Record Settlement')");
        await partnerPage.WaitForURLAsync($"**/h/{householdId}/expenses");

        var tripCard = partnerPage.Locator($"#group-{trip:N}");
        await Assert.That(await tripCard.InnerTextAsync()).Contains("you owe").And.Contains("30.00");
        await Assert.That(await partnerPage.Locator($"#group-{home:N}").InnerTextAsync()).Contains("settled up");
    }

    [Test]
    public async Task Overpaying_a_household_settle_is_blocked()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var trip = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");

        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/settle");
        await partnerPage.FillAsync("input[name='Amount']", "150");
        await partnerPage.Locator(".settle-breakdown-error").WaitForAsync();
        await partnerPage.ClickAsync("button[type='submit']:has-text('Record Settlement')");

        await partnerPage.Locator(".field-error:has-text('To pay extra')").WaitForAsync();
        await Assert.That(partnerPage.Url).Contains("/settle", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task Deleting_a_group_can_be_undone_from_the_flash()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewTrackedPageAsync();
        var trip = await page.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await page.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");

        await page.ClickAsync("summary[aria-label='More actions']");
        await page.ClickAsync("button:has-text('Delete group')");
        await page.ClickAsync("#confirm-ok");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");
        await Assert.That(await page.Locator($"#group-{trip:N}").CountAsync()).IsEqualTo(0);

        await page.ClickAsync(".toast-action-btn:has-text('Undo')");
        await page.Locator($"#group-{trip:N}").WaitForAsync();
    }

    [Test]
    public async Task Deleted_group_can_be_restored_from_activity()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewTrackedPageAsync();
        var trip = await page.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await page.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");
        await page.ClickAsync("summary[aria-label='More actions']");
        await page.ClickAsync("button:has-text('Delete group')");
        await page.ClickAsync("#confirm-ok");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/activity");
        await Assert.That(await page.Locator(".activity-row:has-text('Hotel')").CountAsync()).IsEqualTo(0);
        await page.ClickAsync(".activity-restore-btn");
        await page.Locator(".activity-row:has-text('restored group')").WaitForAsync();
        await Assert.That(await page.Locator(".activity-row:has-text('Hotel')").CountAsync()).IsEqualTo(1);
        await Assert.That(await page.Locator(".activity-restore-btn").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Partner_sees_a_group_card_update_live()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var trip = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Trip");

        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        await partnerPage.WaitForFirstSseRenderAsync();

        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");

        await partnerPage.Locator($"#group-{trip:N} .expenses-group-card-balance.owe")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assert.That(await partnerPage.Locator($"#group-{trip:N}").InnerTextAsync()).Contains("100.00");
    }

    [Test]
    public async Task Home_lists_group_expenses_with_a_chip_until_the_group_is_deleted()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewTrackedPageAsync();
        var trip = await page.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await page.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        var row = page.Locator(".expense-row-header:has-text('Hotel')");
        await row.WaitForAsync();
        await Assert.That(await row.InnerTextAsync()).Contains("Trip");

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups/{trip}");
        await page.ClickAsync("summary[aria-label='More actions']");
        await page.ClickAsync("button:has-text('Delete group')");
        await page.ClickAsync("#confirm-ok");
        await page.WaitForURLAsync($"**/h/{householdId}/expenses");
        await Assert.That(await page.Locator(".expense-row-header:has-text('Hotel')").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Household_settle_shows_once_on_home_with_the_cash_amount()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var trip = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        var home = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Home");
        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");
        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, home, "Groceries", "60");

        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/settle");
        await partnerPage.FillAsync("input[name='Amount']", "40");
        await partnerPage.Locator(".settle-breakdown-summary:has-text('30.00')").WaitForAsync();
        await partnerPage.ClickAsync("button[type='submit']:has-text('Record Settlement')");
        await partnerPage.WaitForURLAsync($"**/h/{householdId}/expenses");

        var payments = partnerPage.Locator(".payment-row");
        await payments.First.WaitForAsync();
        await Assert.That(await payments.CountAsync()).IsEqualTo(1);
        await Assert.That(await payments.First.InnerTextAsync()).Contains("40.00");
    }

    [Test]
    public async Task Group_timeline_collapses_at_the_settled_up_point_and_loads_older_on_demand()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var trip = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Dinner", "100");

        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/settle?group={trip}");
        await partnerPage.Locator("input[name='Amount']").WaitForAsync();
        await partnerPage.ClickAsync("button[type='submit']:has-text('Record Settlement')");
        await partnerPage.WaitForURLAsync(url => ExpensesPageHelpers.IsGroupPage(url, trip));

        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Lunch", "40");

        await Assert.That(await ownerPage.Locator(".expense-row-header:has-text('Lunch')").CountAsync()).IsEqualTo(1);
        await Assert.That(await ownerPage.Locator(".expense-row-header:has-text('Dinner')").CountAsync()).IsEqualTo(0);
        await Assert.That(await ownerPage.Locator(".payment-row").CountAsync()).IsEqualTo(1);

        await ownerPage.ClickAsync("#expenses-show-older");
        await ownerPage.Locator(".expense-row-header:has-text('Dinner')").WaitForAsync();
        await ownerPage.Locator(".payment-row").WaitForAsync();
        await ownerPage.Locator("button#expenses-show-older").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    [Test]
    public async Task Expanded_row_shows_when_it_was_added()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewTrackedPageAsync();
        var trip = await page.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await page.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Hotel", "200");

        await page.Locator(".expense-row-header:has-text('Hotel')").ClickAsync();
        var added = page.Locator(".expense-breakdown-recorded");
        await added.WaitForAsync();
        await Assert.That(await added.InnerTextAsync()).StartsWith("Added");
    }

    [Test]
    public async Task Live_updates_reset_the_loaded_older_entries_and_the_show_earlier_button()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var trip = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Dinner", "100");

        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/settle?group={trip}");
        await partnerPage.Locator("input[name='Amount']").WaitForAsync();
        await partnerPage.ClickAsync("button[type='submit']:has-text('Record Settlement')");
        await partnerPage.WaitForURLAsync(url => ExpensesPageHelpers.IsGroupPage(url, trip));

        await ownerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Lunch", "40");
        await ownerPage.ClickAsync("button#expenses-show-older");
        await ownerPage.Locator(".expense-row-header:has-text('Dinner')").WaitForAsync();
        await ownerPage.Locator("button#expenses-show-older").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // The partner's expense lands in Recent over SSE: the older rows loaded here are cleared and the button is back.
        await partnerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, trip, "Snack", "20");
        await ownerPage.Locator(".expense-row-header:has-text('Snack')").WaitForAsync();
        await ownerPage.Locator("button#expenses-show-older").WaitForAsync();
        await Assert.That(await ownerPage.Locator(".expense-row-header:has-text('Dinner')").CountAsync()).IsEqualTo(0);

        await ownerPage.ClickAsync("button#expenses-show-older");
        await ownerPage.Locator(".expense-row-header:has-text('Dinner')").WaitForAsync();
        await Assert.That(await ownerPage.Locator(".expense-row-header:has-text('Dinner')").CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Members_who_cannot_manage_see_neither_the_delete_menu_nor_restore()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var trip = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Trip");
        await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Home");

        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups/{trip}");
        await partnerPage.Locator(".page-heading").WaitForAsync();
        await Assert.That(await partnerPage.Locator("summary[aria-label='More actions']").CountAsync()).IsEqualTo(0);
        await Assert.That(await ownerPage.Locator("summary[aria-label='More actions']").CountAsync()).IsEqualTo(1);

        await ownerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups/{trip}");
        await ownerPage.ClickAsync("summary[aria-label='More actions']");
        await ownerPage.ClickAsync("button:has-text('Delete group')");
        await ownerPage.ClickAsync("#confirm-ok");
        await ownerPage.WaitForURLAsync($"**/h/{householdId}/expenses");

        await ownerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/activity");
        await Assert.That(await ownerPage.Locator(".activity-restore-btn").CountAsync()).IsEqualTo(1);
        await partnerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/activity");
        await partnerPage.Locator(".activity-row:has-text('Trip')").First.WaitForAsync();
        await Assert.That(await partnerPage.Locator(".activity-restore-btn").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Import_page_links_back_to_the_expenses_home()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Exp HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewTrackedPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/import");
        var back = page.Locator(".page-heading a");
        await Assert.That(await back.GetAttributeAsync("href")).IsEqualTo($"/h/{householdId}/expenses");
    }
}
