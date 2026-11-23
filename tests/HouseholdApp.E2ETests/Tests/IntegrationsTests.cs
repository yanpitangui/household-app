using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
[Category(Shards.Second)]
public class IntegrationsTests(PlaywrightFixture pw)
{
    private static readonly HttpClient Http = new();

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..20];

    private async Task<string> CreateTokenAsync(IPage page, Guid householdId, params string[] scopes)
    {
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/settings/access-tokens");
        await page.FillAsync("input[name=Name]", "Phone");
        foreach (var scope in scopes) await page.CheckAsync($"input[name=Scopes][value='{scope}']");
        await page.ClickAsync("button:has-text('Create token')");
        var card = page.Locator("[data-access-token]");
        await card.WaitForAsync();
        return (await card.GetAttributeAsync("data-access-token"))!;
    }

    private async Task<string> CreateListAsync(IPage page, Guid householdId)
    {
        var name = Unique("Groceries");
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/lists");
        await page.FillAsync("input[name='NewListName']", name);
        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync("button:has-text('Create')"),
            new() { UrlString = "**/lists/**" });
        return name;
    }

    private Task<HttpResponseMessage> PostAsync(string path, string? token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{pw.AppUrl}/api/integrations/{path}")
        {
            Content = JsonContent.Create(body),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Http.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Test]
    public async Task A_token_adds_spoken_items_to_a_named_list_until_it_is_revoked()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, Unique("Tokens"));
        var page = await ctx.NewPageAsync();
        var listName = await CreateListAsync(page, householdId);
        var listUrl = page.Url;
        var token = await CreateTokenAsync(page, householdId, "lists:add");

        var added = await PostAsync("list-items", token, new { text = $"milk and eggs to {listName}" });

        await Assert.That(added.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That((await JsonAsync(added)).GetProperty("message").GetString()).Contains("Milk");
        await page.GotoAsync(listUrl);
        await page.GetByText("Milk", new() { Exact = true }).First.WaitForAsync();
        await page.GetByText("Eggs", new() { Exact = true }).First.WaitForAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/settings/access-tokens");
        await page.ClickAsync("button:has-text('Revoke')");
        await page.RunAndWaitForNavigationAsync(() => page.ClickAsync("#confirm-ok"));

        var rejected = await PostAsync("list-items", token, new { text = $"bread to {listName}" });
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await JsonAsync(rejected)).GetProperty("message").GetString()).IsEqualTo("Invalid access token.");
    }

    [Test]
    public async Task A_token_records_a_spoken_expense_in_the_only_group()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, Unique("Spend"));
        var page = await ctx.NewPageAsync();
        var token = await CreateTokenAsync(page, householdId, "expenses:record");

        var recorded = await PostAsync("expenses", token, new { text = "50 pizza" });

        await Assert.That(recorded.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var json = await JsonAsync(recorded);
        await Assert.That(json.GetProperty("amountCents").GetInt64()).IsEqualTo(5000L);
        await Assert.That(json.GetProperty("description").GetString()).IsEqualTo("pizza");
        await Assert.That(json.GetProperty("group").GetProperty("name").GetString()).IsEqualTo("General");
    }

    [Test]
    public async Task Bad_requests_get_json_errors_never_html()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, Unique("Errors"));
        var page = await ctx.NewPageAsync();
        await CreateListAsync(page, householdId);
        await CreateListAsync(page, householdId);
        var token = await CreateTokenAsync(page, householdId, "lists:add");

        var missing = await PostAsync("list-items", null, new { text = "milk" });
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await JsonAsync(missing)).GetProperty("message").GetString()).IsEqualTo("Missing access token.");

        var garbage = await PostAsync("list-items", "hha_not-a-token", new { text = "milk" });
        await Assert.That(garbage.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        var forbidden = await PostAsync("expenses", token, new { text = "50 pizza" });
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        var unclear = await PostAsync("list-items", token, new { text = "milk" });
        await Assert.That(unclear.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        var issue = (await JsonAsync(unclear)).GetProperty("issues")[0];
        await Assert.That(issue.GetProperty("field").GetString()).IsEqualTo("list");
        await Assert.That(issue.GetProperty("candidates").GetArrayLength()).IsEqualTo(2);
    }
}
