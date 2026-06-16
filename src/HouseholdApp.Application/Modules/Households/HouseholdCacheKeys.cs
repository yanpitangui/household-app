namespace HouseholdApp.Application.Modules.Households;

internal static class HouseholdCacheKeys
{
    internal static string Members(Guid householdId) => $"household-members:{householdId}";
}
