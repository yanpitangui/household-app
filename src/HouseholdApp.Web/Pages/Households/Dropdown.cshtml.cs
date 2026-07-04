using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Households;

public class HouseholdDropdownModel(ICurrentUser currentUser, IHouseholdQueriesWithLastModified householdQueriesWithLastModified)
    : AuthenticatedPageModel(currentUser)
{
    public IReadOnlyList<HouseholdName> Households { get; private set; } = [];
    public Guid CurrentId { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? currentId)
    {
        CurrentId = currentId ?? Guid.Empty;
        var result = await householdQueriesWithLastModified.ListNamesWithLastModifiedAsync(CurrentUserId);
        Households = result.Value;
        return this.NotModifiedOr304(result.LastModified) ?? Page();
    }
}
