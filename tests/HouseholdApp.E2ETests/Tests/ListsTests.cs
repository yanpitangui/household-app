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

        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync("button:has-text('Create')"),
            new() { UrlString = "**/lists/**" });
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
        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync("button:has-text('Create')"),
            new() { UrlString = "**/lists/**" });

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

        // selectCatalogItem fills the fields and auto-submits; the form resets after the request. Recording the
        // fields at htmx:beforeRequest captures exactly what was submitted, whatever the network timing.
        await page.EvaluateAsync(@"() => document.body.addEventListener('htmx:beforeRequest', e => {
            if (e.detail.elt.id !== 'add-item-form') return;
            window.__submittedItem = [
                document.getElementById('item-name-input').value,
                document.getElementById('catalog-item-id').value,
                document.getElementById('category-select-desktop').value
            ];
        })");
        await firstSuggestion.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__submittedItem !== undefined");
        var values = await page.EvaluateAsync<string[]>("() => window.__submittedItem");

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
    public async Task Adding_item_with_quantity_and_unit_shows_them_next_to_name()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        var itemName = $"QtyItem{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, itemName);
        await page.FillAsync("#item-quantity-input", "2");
        await page.FillAsync("#item-unit-input", "kg");
        await page.WaitForTimeoutAsync(500);

        await SubmitAddItemFormAsync(page);
        var itemSpan = page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName });
        await itemSpan.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        var text = await itemSpan.InnerTextAsync();
        await Assert.That(text).Contains("2");
        await Assert.That(text).Contains("kg");

        // form reset after add — quantity/unit inputs must be cleared so they don't leak into the next item
        await Assert.That(await page.InputValueAsync("#item-quantity-input")).IsEmpty();
        await Assert.That(await page.InputValueAsync("#item-unit-input")).IsEmpty();
    }

    [Test]
    public async Task Adding_item_without_quantity_does_not_render_stray_separator()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        var itemName = $"NoQtyItem{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, itemName);
        await page.WaitForTimeoutAsync(500);

        await SubmitAddItemFormAsync(page);
        var itemSpan = page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName });
        await itemSpan.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        var text = (await itemSpan.InnerTextAsync()).Trim();
        await Assert.That(text).IsEqualTo(itemName);
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
    public async Task Item_detail_dialog_edits_quantity_and_unit()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        var itemName = $"EditQtyItem{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, itemName);
        await page.WaitForTimeoutAsync(400);
        await SubmitAddItemFormAsync(page);
        var itemSpan = page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName });
        await itemSpan.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await itemSpan.ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('item-detail-dialog').open === true",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        await page.FillAsync("#detail-quantity-input", "3");
        await page.FillAsync("#detail-unit-input", "kg");
        await page.Locator("#item-detail-dialog button:has-text('Save')").ClickAsync();

        await page.WaitForFunctionAsync("document.getElementById('item-detail-dialog').open === false",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        // SSE re-render is a separate async round-trip (event -> Redis -> stream push) from the
        // dialog closing, so poll for the updated text instead of asserting immediately.
        await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = "kg" })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var updatedSpan = page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName });
        await Assert.That(await updatedSpan.InnerTextAsync()).Contains("3");
        await Assert.That(await updatedSpan.InnerTextAsync()).Contains("kg");

        // reopen the dialog — the saved values must be reflected back into the inputs
        await updatedSpan.ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('item-detail-dialog').open === true",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        await Assert.That(await page.InputValueAsync("#detail-quantity-input")).IsEqualTo("3");
        await Assert.That(await page.InputValueAsync("#detail-unit-input")).IsEqualTo("kg");
    }

    [Test]
    public async Task Manage_categories_dialog_adds_category_to_select()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        await OpenListOptionsMenuAndClickAsync(page, "button:has-text('Manage categories')");
        await page.WaitForFunctionAsync("document.getElementById('category-manage-dialog').open === true",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        // set emoji directly on the hidden input — Bootstrap dropdown unreliable in headless mode
        await page.EvaluateAsync("document.getElementById('cat-form-emoji').value = '🍅'");
        await page.FillAsync("#cat-form-name", "Lab Supplies");
        await page.ClickAsync("#cat-form-save");

        await page.Locator(".category-manage-row").Filter(new LocatorFilterOptions { HasText = "Lab Supplies" })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });

        var options = await page.EvalOnSelectorAsync<string[]>(
            "#category-select", "el => Array.from(el.options).map(o => o.text)");
        await Assert.That(options.Any(o => o.Contains("Lab Supplies"))).IsTrue();
    }

    [Test]
    public async Task Manage_categories_dialog_edits_and_deletes_category()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        await OpenListOptionsMenuAndClickAsync(page, "button:has-text('Manage categories')");
        await page.WaitForFunctionAsync("document.getElementById('category-manage-dialog').open === true",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        await page.EvaluateAsync("document.getElementById('cat-form-emoji').value = '🥕'");
        await page.FillAsync("#cat-form-name", "Editable Category");
        await page.ClickAsync("#cat-form-save");
        var row = page.Locator(".category-manage-row").Filter(new LocatorFilterOptions { HasText = "Editable Category" });
        await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });

        await row.Locator("button", new LocatorLocatorOptions { HasText = "✎" }).ClickAsync();
        await page.FillAsync("#cat-form-name", "Renamed Category");
        await page.ClickAsync("#cat-form-save");
        await page.Locator(".category-manage-row").Filter(new LocatorFilterOptions { HasText = "Renamed Category" })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });

        await page.Locator(".category-manage-row").Filter(new LocatorFilterOptions { HasText = "Renamed Category" })
            .Locator("button", new LocatorLocatorOptions { HasText = "✕" }).ClickAsync();
        await page.ClickAsync("#confirm-ok");
        await page.Locator(".category-manage-row").Filter(new LocatorFilterOptions { HasText = "Renamed Category" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5_000 });

        await Assert.That(
            await page.Locator(".category-manage-row").Filter(new LocatorFilterOptions { HasText = "Renamed Category" }).IsVisibleAsync()
        ).IsFalse();
    }

    [Test]
    public async Task Mark_all_as_done_completes_every_active_item()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);

        var itemA = $"MarkAllA{Guid.NewGuid().ToString("N")[..6]}";
        var itemB = $"MarkAllB{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, itemA);
        await SubmitAddItemFormAsync(page);
        await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemA })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await TypeIntoItemInput(page, itemB);
        await SubmitAddItemFormAsync(page);
        await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemB })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await OpenListOptionsMenuAndClickAsync(page, "button:has-text('Mark all as done')");

        await page.Locator(".check-item.done").Filter(new LocatorFilterOptions { HasText = itemA })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assert.That(
            await page.Locator(".check-item.done").Filter(new LocatorFilterOptions { HasText = itemB }).IsVisibleAsync()
        ).IsTrue();
    }

    [Test]
    public async Task Hide_done_items_persists_across_reload()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page = await ctx.NewPageAsync();
        await NavigateToListAsync(page, householdId);
        var listUrl = page.Url;

        var itemName = $"HideDone{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page, itemName);
        await SubmitAddItemFormAsync(page);
        var itemSpan = page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName });
        await itemSpan.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await page.Locator($".check-item:has(.check-item-text:has-text('{itemName}')) .check-box").ClickAsync();
        await page.Locator(".check-item.done").Filter(new LocatorFilterOptions { HasText = itemName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await OpenListOptionsMenuAndClickAsync(page, "#toggle-done-visibility-btn");
        await page.Locator("#done-items-card").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5_000 });

        await page.GotoAsync(listUrl);
        await page.Locator("#done-items-card").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
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

    [Test]
    public async Task Options_menu_stays_open_when_list_updates_live()
    {
        await using var ctx = await pw.NewAuthenticatedContextAsync();
        var householdId = await pw.CreateHouseholdAsync(ctx, $"Lists HH {Guid.NewGuid().ToString("N")[..8]}");
        var page1 = await ctx.NewPageAsync();
        await NavigateToListAsync(page1, householdId);
        var page2 = await ctx.NewPageAsync();
        await page2.GotoAsync(page1.Url);

        await page1.Locator(".list-options-menu > button").ClickAsync();
        var menu = page1.Locator(".list-options-menu .dropdown-menu");
        await menu.WaitForAsync();

        var itemName = $"LiveItem{Guid.NewGuid().ToString("N")[..6]}";
        await TypeIntoItemInput(page2, itemName);
        await SubmitAddItemFormAsync(page2);
        await page1.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = itemName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await Assert.That(await menu.IsVisibleAsync()).IsTrue();
    }

    private static async Task OpenListOptionsMenuAndClickAsync(IPage page, string itemSelector)
    {
        await page.Locator(".list-options-menu > button").ClickAsync();
        await page.ClickAsync(itemSelector);
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
        await page.RunAndWaitForNavigationAsync(
            () => page.ClickAsync("button:has-text('Create')"),
            new() { UrlString = "**/lists/**" });
    }
}
