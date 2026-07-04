namespace HouseholdApp.Application.Modules.Identity;

internal static class UserCacheKeys
{
    internal static string ById(Guid userId) => $"user-by-id:{userId}";
    internal static string BySubject(string subject) => $"user-by-subject:{subject}";
}
