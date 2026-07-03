using HouseholdApp.E2ETests.Infrastructure;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public class PushNotificationsTests(PlaywrightFixture pw)
{
    // Real Web Push (PushManager.subscribe against an actual push service) needs network
    // access to Google/Mozilla infrastructure and isn't reliable in CI/sandboxes, so this
    // stubs PushManager itself before the page's own scripts run. It exercises our app code
    // (banner state, click handlers, the /api/push/* endpoints) without depending on real
    // push delivery — actual delivery is verified manually (see the feature's design spec).
    // State is kept in localStorage (not a JS closure) so it survives page reloads, matching
    // how a real browser-level subscription persists across navigations.
    private const string FakePushScript = """
        navigator.serviceWorker.ready.then((reg) => {
          reg.pushManager.subscribe = async () => {
            const endpoint = 'https://fake-push.example/' + Math.random().toString(36).slice(2);
            localStorage.setItem('__fakePushSubscription', JSON.stringify({ endpoint, p256dh: 'fake-p256dh', auth: 'fake-auth' }));
            return {
              endpoint,
              toJSON: () => ({ endpoint, keys: { p256dh: 'fake-p256dh', auth: 'fake-auth' } }),
              unsubscribe: async () => { localStorage.removeItem('__fakePushSubscription'); return true; },
            };
          };
          reg.pushManager.getSubscription = async () => {
            const raw = localStorage.getItem('__fakePushSubscription');
            if (!raw) return null;
            const data = JSON.parse(raw);
            return {
              endpoint: data.endpoint,
              toJSON: () => ({ endpoint: data.endpoint, keys: { p256dh: data.p256dh, auth: data.auth } }),
              unsubscribe: async () => { localStorage.removeItem('__fakePushSubscription'); return true; },
            };
          };
        });
        """;

    private async Task<Microsoft.Playwright.IPage> NewPreparedPageAsync(Microsoft.Playwright.IBrowserContext ctx, Guid householdId)
    {
        await ctx.GrantPermissionsAsync(["notifications"]);
        var page = await ctx.NewPageAsync();
        await page.AddInitScriptAsync(FakePushScript);
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}");
        return page;
    }

    [Test]
    public async Task Banner_shows_prompt_state_when_not_subscribed()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, $"Push Test {Guid.NewGuid().ToString("N")[..8]}");
        var page = await NewPreparedPageAsync(ctx, id);

        await Assert.That(await page.Locator("#push-opt-in-banner").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("#push-enable-btn").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("#push-enabled-text").IsVisibleAsync()).IsFalse();
    }

    [Test]
    public async Task Enable_notifications_subscribes_and_shows_enabled_state()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, $"Push Test {Guid.NewGuid().ToString("N")[..8]}");
        var page = await NewPreparedPageAsync(ctx, id);

        var subscribeResponse = page.WaitForResponseAsync(r => r.Url.Contains("/api/push/subscribe"));
        await page.ClickAsync("#push-enable-btn");
        var response = await subscribeResponse;

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(await page.Locator("#push-enabled-text").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("#push-enable-btn").IsVisibleAsync()).IsFalse();
        await Assert.That(await page.Locator("#push-dismiss-btn").IsVisibleAsync()).IsFalse();
    }

    [Test]
    public async Task Enabled_state_survives_reload()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, $"Push Test {Guid.NewGuid().ToString("N")[..8]}");
        var page = await NewPreparedPageAsync(ctx, id);

        var subscribeResponse = page.WaitForResponseAsync(r => r.Url.Contains("/api/push/subscribe"));
        await page.ClickAsync("#push-enable-btn");
        await subscribeResponse;

        await page.ReloadAsync();

        await Assert.That(await page.Locator("#push-enabled-text").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("#push-enable-btn").IsVisibleAsync()).IsFalse();
    }

    [Test]
    public async Task Disable_notifications_unsubscribes_and_returns_to_prompt_state()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, $"Push Test {Guid.NewGuid().ToString("N")[..8]}");
        var page = await NewPreparedPageAsync(ctx, id);

        var subscribeResponse = page.WaitForResponseAsync(r => r.Url.Contains("/api/push/subscribe"));
        await page.ClickAsync("#push-enable-btn");
        await subscribeResponse;

        var unsubscribeResponse = page.WaitForResponseAsync(r => r.Url.Contains("/api/push/unsubscribe"));
        await page.ClickAsync("#push-disable-btn");
        var response = await unsubscribeResponse;

        await Assert.That(response.Status).IsEqualTo(204);
        await Assert.That(await page.Locator("#push-enable-btn").IsVisibleAsync()).IsTrue();
        await Assert.That(await page.Locator("#push-enabled-text").IsVisibleAsync()).IsFalse();

        // Confirm it stays disabled after a reload too, not just in the current DOM state.
        await page.ReloadAsync();
        await Assert.That(await page.Locator("#push-enable-btn").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Dismiss_hides_banner_and_it_stays_hidden_after_reload()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var id = await pw.CreateHouseholdAsync(ctx, $"Push Test {Guid.NewGuid().ToString("N")[..8]}");
        var page = await NewPreparedPageAsync(ctx, id);

        await page.ClickAsync("#push-dismiss-btn");
        await Assert.That(await page.Locator("#push-opt-in-banner").IsVisibleAsync()).IsFalse();

        await page.ReloadAsync();
        await Assert.That(await page.Locator("#push-opt-in-banner").IsVisibleAsync()).IsFalse();
    }
}
