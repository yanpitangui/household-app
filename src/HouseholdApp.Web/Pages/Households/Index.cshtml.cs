using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Pages.Households;

public class HouseholdsIndexModel(ICurrentUser currentUser, IHouseholdQueries householdQueries)
    : AuthenticatedPageModel(currentUser)
{
    public IReadOnlyList<HouseholdSummary> Households { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Households = await householdQueries.ListForUserAsync(CurrentUserId);
    }
}
