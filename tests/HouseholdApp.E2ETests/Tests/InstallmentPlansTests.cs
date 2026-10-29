using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
public class InstallmentPlansTests(PlaywrightFixture pw)
{
    private static bool IsPlanPage(string url) => url.Contains("/expenses/plans/", StringComparison.OrdinalIgnoreCase);

    // Fills the Record form's Repeat panel for an N-installment plan and returns the new plan's id from the plan page URL.
    private static async Task<Guid> RecordPlanAsync(
        IPage page, string appUrl, Guid householdId, Guid groupId, string description, string amount, int count, DateOnly firstDate)
    {
        await page.GotoAsync($"{appUrl}/h/{householdId}/expenses/record?group={groupId}");
        await page.FillAsync("input[name='Description']", description);
        await page.FillAsync("input[name='Amount']", amount);
        await page.ClickAsync("#open-repeat-btn");
        await page.ClickAsync(".repeat-seg-opt[data-repeat='installments']");
        for (var i = 2; i < count; i++) await page.ClickAsync(".repeat-stepper button:has-text('+')");
        await page.FillAsync("#first-date", firstDate.ToString("yyyy-MM-dd"));
        await page.ClickAsync("#panel-c .split-header-done");
        await Assert.That(await page.Locator("#record-save-btn").InnerTextAsync()).Contains($"{count} installments");
        await page.ClickAsync("#record-save-btn");
        await page.WaitForURLAsync(IsPlanPage);
        return Guid.Parse(new Uri(page.Url).Segments[^1]);
    }

    [Test]
    public async Task A_plan_underway_creates_past_installments_and_can_be_paid_early()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Inst HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var page = await ownerCtx.NewTrackedPageAsync();
        var car = await page.CreateGroupAsync(pw.AppUrl, householdId, "Car");

        // First installment a month ago: #1 and #2 (due today) are created on save, #3 stays upcoming.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await RecordPlanAsync(page, pw.AppUrl, householdId, car, "Car repair", "900", 3, today.AddMonths(-1));

        await Assert.That(await page.Locator(".plan-slot.created").CountAsync()).IsEqualTo(2);
        await Assert.That(await page.Locator(".plan-kv").First.InnerTextAsync()).Contains("600.00").And.Contains("300.00");

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups/{car}");
        await Assert.That(await page.Locator(".installment-pill:has-text('2/3')").CountAsync()).IsEqualTo(1);

        await page.Locator(".expense-row-header:has-text('Car repair')").First.ClickAsync();
        await page.Locator(".expense-row-wrap.expanded a.plan-view-link").First.ClickAsync();
        await page.WaitForURLAsync(IsPlanPage);

        await page.ClickAsync("button:has-text('Pay remaining early')");
        await page.FillAsync("#payoff-amount", "250");
        await Assert.That(await page.Locator("#payoff-discount").InnerTextAsync()).Contains("50.00");
        await page.ClickAsync("#payoff-submit");

        await page.Locator(".plan-status:has-text('Paid early')").WaitForAsync();
        await Assert.That(await page.Locator(".activity-row:has-text('early')").CountAsync()).IsGreaterThan(0);
    }

    [Test]
    public async Task Upcoming_strip_sums_the_next_installment_and_remembers_it_is_open()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Inst HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var page = await ownerCtx.NewTrackedPageAsync();
        var home = await page.CreateGroupAsync(pw.AppUrl, householdId, "Home");

        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        await RecordPlanAsync(page, pw.AppUrl, householdId, home, "New fridge", "1200", 4, soon);

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses");
        var strip = page.Locator("#upcoming");
        await Assert.That(await strip.InnerTextAsync()).Contains("300.00").And.Contains("150.00");
        await Assert.That(await page.Locator(".upcoming-row").First.IsVisibleAsync()).IsFalse();

        await page.ClickAsync(".upcoming-toggle");
        await Assert.That(await page.Locator(".upcoming-row:has-text('New fridge')").IsVisibleAsync()).IsTrue();

        await page.ReloadAsync();
        await page.Locator("#upcoming.expanded").WaitForAsync();
        await Assert.That(await page.Locator(".upcoming-row:has-text('New fridge')").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Live_updates_keep_the_plan_link_and_the_open_upcoming_strip()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ownerCtx, $"Inst HH {Guid.NewGuid().ToString("N")[..8]}");
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, householdId);
        var ownerPage = await ownerCtx.NewTrackedPageAsync();
        var home = await ownerPage.CreateGroupAsync(pw.AppUrl, householdId, "Home");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pastPlan = await RecordPlanAsync(ownerPage, pw.AppUrl, householdId, home, "Sofa", "600", 3, today.AddMonths(-1));
        await RecordPlanAsync(ownerPage, pw.AppUrl, householdId, home, "New fridge", "1200", 4, today.AddDays(5));

        await ownerPage.GotoAsync($"{pw.AppUrl}/h/{householdId}/expenses/groups/{home}");
        await ownerPage.WaitForFirstSseRenderAsync();
        await ownerPage.ClickAsync(".upcoming-toggle");
        await ownerPage.Locator(".expense-row-header:has-text('Sofa')").First.ClickAsync();
        var viewLink = ownerPage.Locator(".expense-row-wrap.expanded a.plan-view-link").First;
        await Assert.That(await viewLink.GetAttributeAsync("href")).EndsWith($"/Expenses/Plans/{pastPlan}", StringComparison.OrdinalIgnoreCase);

        // The partner's unrelated expense in the same group pushes a fresh render to the owner's open page.
        var partnerPage = await partnerCtx.NewTrackedPageAsync();
        await partnerPage.RecordEqualSplitAsync(pw.AppUrl, householdId, home, "Snacks", "20");
        await ownerPage.Locator(".expense-row-header:has-text('Snacks')").WaitForAsync();

        await Assert.That(await ownerPage.Locator("#upcoming.expanded").CountAsync()).IsEqualTo(1);
        await Assert.That(await ownerPage.Locator(".upcoming-row:has-text('New fridge')").IsVisibleAsync()).IsTrue();
        await Assert.That(await ownerPage.Locator(".expense-row-wrap.expanded a.plan-view-link").First.GetAttributeAsync("href"))
            .EndsWith($"/Expenses/Plans/{pastPlan}", StringComparison.OrdinalIgnoreCase);
    }
}
