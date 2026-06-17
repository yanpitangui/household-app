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

        await Assert.That(await page.InputValueAsync("#item-name-input")).IsEqualTo(suggestedName);
        await Assert.That(await page.InputValueAsync("#catalog-item-id")).IsNotEmpty();
        var selectedCategory = await page.EvalOnSelectorAsync<string>("#category-select", "el => el.value");
        await Assert.That(selectedCategory).IsNotEmpty();
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

        await SubmitAddItemFormAsync(page);
        await page.Locator(".check-item-text").Filter(new LocatorFilterOptions { HasText = suggestedName })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        var badge = page.Locator(".check-item")
            .Filter(new LocatorFilterOptions { HasText = suggestedName })
            .Locator(".list-row-badge");
        await Assert.That(await badge.IsVisibleAsync()).IsTrue();
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
        await page.FillAsync("#new-cat-emoji", "🧪");
        await page.FillAsync("#new-cat-name", "Lab Supplies");
        await page.ClickAsync("#new-category-form button:has-text('Save')");

        await page.WaitForTimeoutAsync(1_000);
        var options = await page.EvalOnSelectorAsync<string[]>(
            "#category-select", "el => Array.from(el.options).map(o => o.text)");

        await Assert.That(options.Any(o => o.Contains("Lab Supplies"))).IsTrue();
    }

    private static async Task TypeIntoItemInput(IPage page, string text)
    {
        await page.ClickAsync("#item-name-input");
        await page.Locator("#item-name-input").PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = 80 });
    }

    private static async Task SubmitAddItemFormAsync(IPage page)
    {
        await page.ClickAsync("button.btn-primary[hx-post]");
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
