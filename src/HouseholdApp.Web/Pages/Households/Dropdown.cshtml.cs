using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Pages.Households;

public class HouseholdDropdownModel(ICurrentUser currentUser, IHouseholdQueries householdQueries)
    : AuthenticatedPageModel(currentUser)
{
    public IReadOnlyList<HouseholdName> Households { get; private set; } = [];
    public Guid CurrentId { get; private set; }

    public async Task OnGetAsync(Guid? currentId)
    {
        CurrentId = currentId ?? Guid.Empty;
        Households = await householdQueries.ListNamesAsync(CurrentUserId);
    }
}
