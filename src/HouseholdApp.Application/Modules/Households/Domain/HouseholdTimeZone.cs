namespace HouseholdApp.Application.Modules.Households.Domain;

public static class HouseholdTimeZone
{
    public const string Default = "America/Sao_Paulo";

    /// <summary>True for zone ids the server knows (IANA ids on Linux, e.g. "America/Sao_Paulo").</summary>
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
