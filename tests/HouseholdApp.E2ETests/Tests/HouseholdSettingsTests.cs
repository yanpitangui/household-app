using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[Category(Shards.Second)]
public class HouseholdSettingsTests(PlaywrightFixture pw)
{
    private string SettingsUrl(Guid id) => $"{pw.AppUrl}/h/{id}/settings";

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..20];

    private async Task<IPage> OpenSettingsAsync(IBrowserContext ctx, Guid id)
    {
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(SettingsUrl(id));
        return page;
    }

    [Test]
    public async Task Owner_renames_household_from_the_dashboard_gear_and_empty_names_are_rejected()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, Unique("Rename"));
        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{pw.AppUrl}/h/{id}");
        await page.Locator("a.household-settings-link").ClickAsync();
        await page.WaitForURLAsync(SettingsUrl(id));

        await page.FillAsync("input[name=Name]", "");
        await page.Locator("form:has(input[name=Name]) button[type=submit]").ClickAsync();
        await page.Locator(".field-error:has-text('required')").WaitForAsync();
        await page.GotoAsync(SettingsUrl(id));
        await Assert.That(await page.InputValueAsync("input[name=Name]")).StartsWith("Rename");

        await page.FillAsync("input[name=Name]", "Casa Nova");
        await page.Locator("form:has(input[name=Name]) button[type=submit]").ClickAsync();
        await page.Locator(".toast-title:has-text('Household renamed.')").WaitForAsync();
        await Assert.That(await page.InputValueAsync("input[name=Name]")).IsEqualTo("Casa Nova");
    }

    [Test]
    public async Task Owner_changes_the_time_zone()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, Unique("Zone"));
        var page = await OpenSettingsAsync(ctx, id);

        await page.SelectOptionAsync("select[name=TimeZone]", "Europe/Lisbon");
        await page.Locator("form:has(select[name=TimeZone]) button[type=submit]").ClickAsync();
        await page.Locator(".toast-title:has-text('Time zone updated.')").WaitForAsync();

        await page.GotoAsync(SettingsUrl(id));
        await Assert.That(await page.InputValueAsync("select[name=TimeZone]")).IsEqualTo("Europe/Lisbon");
    }

    [Test]
    public async Task Invite_card_survives_one_redirect_and_pending_invites_can_be_revoked()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, Unique("Invite"));
        var page = await OpenSettingsAsync(ctx, id);

        await page.ClickAsync("button:has-text('Generate invite link')");
        await page.Locator(".invite-card").WaitForAsync();
        await Assert.That(await page.Locator("button:has-text('Revoke')").CountAsync()).IsEqualTo(1);

        // Reloading the redirected page shows no card and mints no second invite.
        await page.ReloadAsync();
        await Assert.That(await page.Locator(".invite-card").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("button:has-text('Revoke')").CountAsync()).IsEqualTo(1);

        await page.Locator("button:has-text('Revoke')").ClickAsync();
        await page.Locator(".toast-title:has-text('Invite revoked.')").WaitForAsync();
        await Assert.That(await page.Locator("button:has-text('Revoke')").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Roles_change_and_a_plain_member_gets_no_management_controls()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ownerCtx, Unique("Roles"));
        await using var memberCtx = await pw.AddPartnerAsync(ownerCtx, id);
        var ownerPage = await OpenSettingsAsync(ownerCtx, id);
        var memberRow = ownerPage.Locator("[data-member]:has-text('Partner User')");

        // The owner has no Leave link until ownership moves.
        await Assert.That(await ownerPage.Locator("a:has-text('Leave household')").CountAsync()).IsEqualTo(0);
        await Assert.That(await ownerPage.GetByText("Transfer ownership").CountAsync()).IsGreaterThan(0);

        await memberRow.Locator("button:has-text('Make admin')").ClickAsync();
        await ownerPage.Locator(".toast-title:has-text('Role updated.')").WaitForAsync();
        await Assert.That(await memberRow.InnerTextAsync()).Contains("Admin");

        await memberRow.Locator("button:has-text('Make member')").ClickAsync();
        await ownerPage.Locator("[data-member]:has-text('Partner User') button:has-text('Make admin')").WaitForAsync();

        var memberPage = await OpenSettingsAsync(memberCtx, id);
        await Assert.That(await memberPage.Locator("input[name=Name]").CountAsync()).IsEqualTo(0);
        await Assert.That(await memberPage.Locator("button:has-text('Generate invite link')").CountAsync()).IsEqualTo(0);
        await Assert.That(await memberPage.Locator("a:has-text('Remove')").CountAsync()).IsEqualTo(0);
        await Assert.That(await memberPage.Locator("button:has-text('Make')").CountAsync()).IsEqualTo(0);
        await Assert.That(await memberPage.Locator("a:has-text('Leave household')").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Removing_a_member_lists_the_recurring_split_change_then_removes_them()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ownerCtx, Unique("Remove"));
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, id);
        await using var thirdCtx = await pw.AddMemberAsync(ownerCtx, id, E2EUser.Third);

        // Rent is paid by the owner and split three ways, so the partner's share goes to the others on removal.
        var page = await ownerCtx.NewPageAsync();
        var groupId = await page.CreateGroupAsync(pw.AppUrl, id, "Home");
        await page.GotoAsync($"{pw.AppUrl}/h/{id}/expenses/record?group={groupId}&repeat=recurring");
        await page.FillAsync("input[name='Description']", "Rent");
        await page.FillAsync("input[name='Amount']", "900");
        await page.SelectOptionAsync("#RecurringFrequency", "Monthly");
        await page.FillAsync("#RecurringFirstDate", DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"));
        await page.FillAsync("#RecurringTime", "12:00");
        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync("#record-save-btn"),
            new() { UrlFunc = url => url.Contains("/expenses/recurring/", StringComparison.OrdinalIgnoreCase) });

        await page.GotoAsync(SettingsUrl(id));
        await page.Locator("[data-member]:has-text('Partner User') a:has-text('Remove')").ClickAsync();
        await page.WaitForURLAsync("**/settings/remove/**");
        var consequences = page.Locator("ul.removal-consequences");
        await Assert.That(await consequences.InnerTextAsync())
            .Contains("split Partner User's share of \"Rent\" among the others");

        await page.Locator("form button.btn-danger").ClickAsync();
        await page.WaitForURLAsync(SettingsUrl(id));
        await page.Locator(".toast-title:has-text('removed')").WaitForAsync();
        await Assert.That(await page.Locator("[data-member]:has-text('Partner User')").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("[data-member]:has-text('Third User')").CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task A_member_leaves_and_the_household_disappears_from_their_list()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var name = Unique("Leave");
        var id = await pw.CreateHouseholdAsync(ownerCtx, name);
        await using var memberCtx = await pw.AddPartnerAsync(ownerCtx, id);
        var page = await OpenSettingsAsync(memberCtx, id);

        await page.Locator("a:has-text('Leave household')").ClickAsync();
        await page.WaitForURLAsync("**/settings/leave");
        await page.Locator("form button.btn-danger").ClickAsync();
        await page.WaitForURLAsync($"{pw.AppUrl}/households");

        await Assert.That(await page.Locator($"text={name}").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task A_former_member_with_an_open_balance_stays_settleable_and_is_tagged()
    {
        await using var ownerCtx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ownerCtx, Unique("Former"));
        await using var partnerCtx = await pw.AddPartnerAsync(ownerCtx, id);
        var page = await ownerCtx.NewTrackedPageAsync();
        var groupId = await page.CreateGroupAsync(pw.AppUrl, id, "Home");
        await page.RecordEqualSplitAsync(pw.AppUrl, id, groupId, "Shared dinner", "100");

        await page.GotoAsync($"{SettingsUrl(id)}");
        await page.Locator("[data-member]:has-text('Partner User') a:has-text('Remove')").ClickAsync();
        await page.WaitForURLAsync("**/settings/remove/**");
        await page.Locator("form button.btn-danger").ClickAsync();
        await page.WaitForURLAsync(SettingsUrl(id));

        await page.GotoAsync($"{pw.AppUrl}/h/{id}/expenses/groups/{groupId}");
        var row = page.Locator("#expenses-balance-card .expenses-person-row");
        await row.WaitForAsync();
        await Assert.That(await row.InnerTextAsync()).Contains("former member").And.Contains("50.00");

        await page.GotoAsync($"{pw.AppUrl}/h/{id}/expenses/settle?group={groupId}");
        await Assert.That(await page.Locator("select[name='PayerId'] option:has-text('Partner User')").CountAsync()).IsEqualTo(1);
        await Assert.That(await page.Locator("select[name='PayerId'] option:checked").InnerTextAsync()).IsEqualTo("Partner User");
    }
}
