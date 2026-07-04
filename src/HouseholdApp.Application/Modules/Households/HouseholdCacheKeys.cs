namespace HouseholdApp.Application.Modules.Households;

internal static class HouseholdCacheKeys
{
    internal static string Members(Guid householdId) => $"household-members:{householdId}";
    internal static string Detail(Guid householdId) => $"household-detail:{householdId}";
    internal static string ListForUser(Guid userId) => $"household-list-for-user:{userId}";
    internal static string ListNames(Guid userId) => $"household-list-names:{userId}";
}
