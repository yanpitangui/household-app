namespace HouseholdApp.E2ETests.Infrastructure;

public enum E2EUser
{
    /// <summary>"Test User" — creates the households in tests.</summary>
    Owner,
    /// <summary>"Partner User" — joins a household through <see cref="PlaywrightFixture.AddPartnerAsync"/>.</summary>
    Partner,
    /// <summary>"Third User" — a third member for splits among more than two, joined like the partner.</summary>
    Third,
}

public sealed class PlaywrightFixture : IAsyncInitializer, IAsyncDisposable
{
    [ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
    public required AppFixture App { get; init; }

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly Dictionary<E2EUser, string> _storageStatePaths = [];

    public string AppUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH")))
        {
            var asmDir = Path.GetDirectoryName(typeof(PlaywrightFixture).Assembly.Location)!;
            Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", asmDir);
        }

        using var httpClient = App.CreateHttpClient("web", "http");
        AppUrl = httpClient.BaseAddress!.AbsoluteUri.TrimEnd('/');

        Console.WriteLine($"[E2E] AppUrl      = {AppUrl}");
        Console.WriteLine($"[E2E] FakeOidc    = {App.FakeOidc.Url}");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        foreach (var user in Enum.GetValues<E2EUser>())
            _storageStatePaths[user] = await LoginAndSaveStateAsync(user);
    }

    private async Task<string> LoginAndSaveStateAsync(E2EUser user)
    {
        var path = Path.Combine(Path.GetTempPath(), $"e2e-auth-{user}-{Guid.NewGuid()}.json");

        await using var ctx = await _browser!.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        ctx.Console += (_, e) => Console.WriteLine($"[browser:{user}] {e.Type}: {e.Text}");

        // The fake OIDC server signs in whichever user this cookie names on its own origin.
        await ctx.AddCookiesAsync([new Cookie
        {
            Name = FakeOidcUser.CookieName,
            Value = OidcUserFor(user).Subject,
            Url = App.FakeOidc.Url,
        }]);

        var page = await ctx.NewPageAsync();
        page.Response += (_, r) => Console.WriteLine($"[browser:{user}] {r.Status} {r.Url}");

        await LoginAsync(page);
        await ctx.StorageStateAsync(new BrowserContextStorageStateOptions { Path = path });
        return path;
    }

    private static FakeOidcUser OidcUserFor(E2EUser user) => user switch
    {
        E2EUser.Owner => FakeOidcUser.Owner,
        E2EUser.Partner => FakeOidcUser.Partner,
        E2EUser.Third => FakeOidcUser.Third,
        _ => throw new ArgumentOutOfRangeException(nameof(user), user, null),
    };

    private async Task LoginAsync(IPage page)
    {
        Console.WriteLine($"[E2E] Navigating to {AppUrl}/households ...");
        await page.GotoAsync($"{AppUrl}/households", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Commit,
            Timeout = 60_000,
        });
        Console.WriteLine($"[E2E] Committed, current URL = {page.Url}");
        await page.WaitForURLAsync($"{AppUrl}/households", new PageWaitForURLOptions { Timeout = 90_000 });
        Console.WriteLine($"[E2E] Login complete");
    }

    public async Task<IBrowserContext> NewAuthenticatedContextAsync(E2EUser user = E2EUser.Owner)
    {
        return await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = _storageStatePaths[user],
            IgnoreHTTPSErrors = true,
        });
    }

    public Task<IBrowserContext> AddPartnerAsync(IBrowserContext ownerCtx, Guid householdId) =>
        AddMemberAsync(ownerCtx, householdId, E2EUser.Partner);

    /// <summary>
    /// Invites <paramref name="member"/> into <paramref name="householdId"/> from the owner's context and accepts
    /// the invite as them. Returns the member's context, which the caller disposes.
    /// </summary>
    public async Task<IBrowserContext> AddMemberAsync(IBrowserContext ownerCtx, Guid householdId, E2EUser member)
    {
        var ownerPage = await ownerCtx.NewPageAsync();
        await ownerPage.GotoAsync($"{AppUrl}/h/{householdId}");
        await ownerPage.ClickAsync("button:has-text('Generate invite link')");
        var inviteButton = ownerPage.Locator("[data-invite-url]").First;
        await inviteButton.WaitForAsync();
        var joinUrl = await inviteButton.GetAttributeAsync("data-invite-url")
            ?? throw new InvalidOperationException("Invite card rendered without a data-invite-url");
        await ownerPage.CloseAsync();

        var memberCtx = await NewAuthenticatedContextAsync(member);
        try
        {
            var memberPage = await memberCtx.NewPageAsync();
            await memberPage.GotoAsync(joinUrl);
            await memberPage.ClickAsync("button[type='submit']:has-text('Accept Invitation')");
            await memberPage.Locator(".alert-info").WaitForAsync();
            await memberPage.CloseAsync();
            return memberCtx;
        }
        catch
        {
            await memberCtx.DisposeAsync();
            throw;
        }
    }

    public async Task<Guid> CreateHouseholdAsync(IBrowserContext ctx, string name)
    {
        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{AppUrl}/households/create");
        await page.FillAsync("input[name='Name']", name);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/h/**", new PageWaitForURLOptions { Timeout = 60_000 });
        var segments = new Uri(page.Url).Segments;
        var id = Guid.Parse(segments.Last().TrimEnd('/'));
        await page.CloseAsync();
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
        foreach (var path in _storageStatePaths.Values)
            if (File.Exists(path)) File.Delete(path);
    }
}
