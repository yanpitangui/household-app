using HouseholdApp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace HouseholdApp.E2ETests.Tests;

[ClassDataSource<PlaywrightFixture>(Shared = SharedType.PerClass)]
public class ListsTests(PlaywrightFixture pw)
{
    [Test]
    public async Task Create_list_appears_in_index()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/lists");

        var listName = $"Weekly groceries {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewListName']", listName);
        await page.ClickAsync("button:has-text('Create')");

        await page.WaitForURLAsync("**/lists/**");
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/lists");

        await Assert.That(await page.Locator($"text={listName}").IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Delete_list_removes_it()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();

        var listName = $"Delete list {Guid.NewGuid().ToString("N")[..8]}";
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/lists");
        await page.FillAsync("input[name='NewListName']", listName);
        await page.ClickAsync("button:has-text('Create')");
        await page.WaitForURLAsync("**/lists/**");

        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/lists");
        await Assert.That(await page.Locator($"text={listName}").IsVisibleAsync()).IsTrue();

        await page.ClickAsync("button:has-text('Delete')");
        await page.ClickAsync("#confirm-ok");
        await page.Locator($"text={listName}").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await Assert.That(await page.Locator($"text={listName}").IsVisibleAsync()).IsFalse();
    }

    [Test]
    public async Task Catalog_suggestions_appear_when_typing()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        await TypeIntoItemInput(page, "Mil");

        await page.Locator("#item-suggestions .suggestion-item").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await Assert.That(await page.Locator("#item-suggestions .suggestion-item").CountAsync()).IsGreaterThan(0);
    }

    [Test]
    public async Task Selecting_suggestion_fills_name_and_category()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        await TypeIntoItemInput(page, "Mil");
        var firstSuggestion = page.Locator("#item-suggestions .suggestion-item").First;
        await firstSuggestion.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var suggestedName = await firstSuggestion.Locator("strong").InnerTextAsync();
        await firstSuggestion.ClickAsync();

        // Read all three values in one atomic JS call. selectCatalogItem fills them
        // synchronously then auto-submits via HTMX (which resets the form on after-request).
        // A single EvaluateAsync round-trip beats the network response; three separate
        // Playwright calls do not.
        var values = await page.EvaluateAsync<string[]>(@"() => [
            document.getElementById('item-name-input').value,
            document.getElementById('catalog-item-id').value,
            document.getElementById('category-select-desktop').value
        ]");

        await Assert.That(values[0]).IsEqualTo(suggestedName);
        await Assert.That(values[1]).IsNotEmpty();
        await Assert.That(values[2]).IsNotEmpty();
    }

    [Test]
    public async Task Adding_item_via_suggestion_shows_category_badge()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        await TypeIntoItemInput(page, "Mil");
        var firstSuggestion = page.Locator("#item-suggestions .suggestion-item").First;
        await firstSuggestion.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var suggestedName = await firstSuggestion.Locator("strong").InnerTextAsync();
        await firstSuggestion.ClickAsync();

        // clicking a suggestion auto-submits via selectCatalogItem → no manual submit needed
        await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = suggestedName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // items with a category are rendered under a category-group-title header, not with a badge
        var categoryTitle = page.Locator(".category-group-title");
        await categoryTitle.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        await Assert.That(await categoryTitle.IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Adding_free_typed_item_without_suggestion()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        var itemName = $"MyCustomItem{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, itemName);
        await page.WaitForTimeoutAsync(500);

        await SubmitAddItemFormAsync(page);
        await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await Assert.That(
            await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName }).IsVisibleAsync()
        ).IsTrue();
    }

    [Test]
    public async Task Free_typed_item_appears_in_autocomplete_on_second_visit()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        var unique = $"Zorbax{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, unique);
        await page.WaitForTimeoutAsync(400);
        await SubmitAddItemFormAsync(page);
        await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = unique })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        await page.FillAsync("#item-name-input", "");
        await TypeIntoItemInput(page, unique[..5]);
        var suggestion = page.Locator("#item-suggestions .suggestion-item")
            .Filter(new LocatorFilterOptions { HasText = unique });
        await suggestion.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await Assert.That(await suggestion.IsVisibleAsync()).IsTrue();
    }

    [Test]
    public async Task Item_detail_dialog_shows_on_click()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        var itemName = $"DetailItem{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, itemName);
        await page.WaitForTimeoutAsync(400);
        await SubmitAddItemFormAsync(page);
        var itemSpan = page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName });
        await itemSpan.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        await itemSpan.ClickAsync();

        await page.WaitForFunctionAsync("document.getElementById('item-detail-dialog').open === true",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        await Assert.That(await page.Locator("#detail-name").InnerTextAsync()).IsEqualTo(itemName);
    }

    [Test]
    public async Task New_category_inline_form_adds_category_to_select()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        await page.ClickAsync("button:has-text('New category')");
        // set emoji directly on the hidden input — Bootstrap dropdown unreliable in headless mode
        await page.EvaluateAsync("document.getElementById('new-cat-emoji').value = '🍅'");
        await page.FillAsync("#new-cat-name", "Lab Supplies");
        await page.ClickAsync("#new-category-form button:has-text('Save')");

        await page.WaitForTimeoutAsync(1_000);
        var options = await page.EvalOnSelectorAsync<string[]>(
            "#category-select", "el => Array.from(el.options).map(o => o.text)");

        await Assert.That(options.Any(o => o.Contains("Lab Supplies"))).IsTrue();
    }

    [Test]
    public async Task Completing_item_on_one_page_updates_another_page_without_refresh()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"RT HH {Guid.NewGuid().ToString("N")[..8]}");

        var page1 = await ctx.NewPageAsync();
        await NavigateToListAsync(page1, householdId);
        var listUrl = page1.Url;

        var itemName = $"RealtimeItem{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page1, itemName);
        await SubmitAddItemFormAsync(page1);
        await page1.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // Open the same list in a second page — SSE connection opens automatically
        var page2 = await ctx.NewPageAsync();
        await page2.GotoAsync(listUrl);
        await page2.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // Complete the item on page1
        await page1.Locator($".check-item:has(.check-item-text:has-text('{itemName}')) .check-box").ClickAsync();

        // page2 should show it in the done section without any manual refresh
        await page2.Locator(".check-item.done").Filter(new LocatorFilterOptions { HasText = itemName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await Assert.That(
            await page2.Locator(".check-item.done")
                .Filter(new LocatorFilterOptions { HasText = itemName })
                .IsVisibleAsync()
        ).IsTrue();
    }

    private static async Task TypeIntoItemInput(IPage page, string text)
    {
        await page.ClickAsync("#item-name-input");
        await page.Locator("#item-name-input").PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = 80 });
    }

    private static async Task SubmitAddItemFormAsync(IPage page)
    {
        await page.Locator("#item-name-input").PressAsync("Enter");
    }

    private async Task NavigateToListAsync(IPage page, Guid householdId)
    {
        await page.GotoAsync($"{pw.AppUrl}/h/{householdId}/lists");
        var listName = $"E2E List {Guid.NewGuid().ToString("N")[..8]}";
        await page.FillAsync("input[name='NewListName']", listName);
        await page.ClickAsync("button:has-text('Create')");
        await page.WaitForURLAsync("**/lists/**");
    }
}
