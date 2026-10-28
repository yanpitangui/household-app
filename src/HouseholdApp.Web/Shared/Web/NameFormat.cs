namespace HouseholdApp.Web.Shared.Web;

public static class NameFormat
{
    /// <summary>First word of a display name, falling back to the trimmed name, then "?".</summary>
    public static string First(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 0) return parts[0];
        var trimmed = displayName.Trim();
        return trimmed.Length > 0 ? trimmed : "?";
    }
}
