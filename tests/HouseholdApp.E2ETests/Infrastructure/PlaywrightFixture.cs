namespace HouseholdApp.E2ETests.Infrastructure;

public sealed class PlaywrightFixture : IAsyncInitializer, IAsyncDisposable
{
    [ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
    public required AppFixture App { get; init; }

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string _storageStatePath = "";

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
        _storageStatePath = Path.Combine(Path.GetTempPath(), $"e2e-auth-{Guid.NewGuid()}.json");

        await using var ctx = await _browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        ctx.Console += (_, e) => Console.WriteLine($"[browser] {e.Type}: {e.Text}");

        var page = await ctx.NewPageAsync();
        page.Response += (_, r) => Console.WriteLine($"[browser] {r.Status} {r.Url}");

        await LoginAsync(page);
        await ctx.StorageStateAsync(new BrowserContextStorageStateOptions { Path = _storageStatePath });
    }

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

    public async Task<IBrowserContext> NewAuthenticatedContextAsync()
    {
        return await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = _storageStatePath,
            IgnoreHTTPSErrors = true,
        });
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
        if (File.Exists(_storageStatePath)) File.Delete(_storageStatePath);
    }
}
