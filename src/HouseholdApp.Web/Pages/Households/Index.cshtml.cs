using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Pages.Households;

public class HouseholdsIndexModel(ICurrentUser currentUser, IHouseholdQueriesWithLastModified householdQueriesWithLastModified)
    : AuthenticatedPageModel(currentUser)
{
    public IReadOnlyList<HouseholdSummary> Households { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await householdQueriesWithLastModified.ListForUserWithLastModifiedAsync(CurrentUserId);
        Households = result.Value;
        return this.NotModifiedOr304(result.LastModified) ?? Page();
    }
}
