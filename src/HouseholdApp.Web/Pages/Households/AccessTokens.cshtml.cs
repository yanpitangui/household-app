using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Localization;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Households;

// Each member manages their own tokens; a token can do only what its owner can, in this household.
// The raw token appears once in the response, so the page stays out of every cache.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccessTokensModel(
    ICurrentUser currentUser, IHouseholdGuard guard,
    IAccessTokenCommands tokens, IListQueries lists, IExpenseQueries expenses) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public override Guid HouseholdId { get => Id; set => Id = value; }

    [BindProperty, Required, MaxLength(100)] public string Name { get; set; } = "";
    [BindProperty] public List<string> Scopes { get; set; } = [];
    [BindProperty] public Guid? DefaultListId { get; set; }
    [BindProperty] public Guid? DefaultGroupId { get; set; }
    [BindProperty] public int LifetimeDays { get; set; } = 90;

    public IReadOnlyList<AccessTokenSummary> Tokens { get; private set; } = [];
    public IReadOnlyList<ListSummary> Lists { get; private set; } = [];
    public IReadOnlyList<ExpenseGroupSummary> Groups { get; private set; } = [];
    [TempData] public string? CreatedToken { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (Scopes.Count == 0) ModelState.AddModelError(nameof(Scopes), Loc["Settings.AccessTokens.ScopeRequired"]);
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }
        try
        {
            var created = await tokens.CreateAsync(Id, new CreateAccessToken(Name, Scopes, DefaultListId, DefaultGroupId, LifetimeDays));
            CreatedToken = created.Token;
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", Loc.Explain(ex));
            await LoadAsync();
            return Page();
        }
        return RedirectToPage(new { id = Id });   // PRG: the token shows once and a refresh simply reloads the list
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid tokenId)
    {
        if (!await tokens.RevokeAsync(Id, tokenId)) return NotFound();
        TempData["Success"] = Loc["Flash.AccessTokenRevoked"].Value;
        return RedirectToPage(new { id = Id });
    }

    private async Task LoadAsync()
    {
        Tokens = await tokens.ListAsync(Id);
        Lists = await lists.ListAsync(Id);
        Groups = await expenses.ListExpenseGroupsAsync(Id);
    }
}
