using System.Globalization;
using System.Text;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

/// <summary>
/// The emoji a group shows on cards, its page and chips: one user-perceived character whose first code point is a
/// symbol, so emoji with skin tones, flags and family sequences fit while letters, digits and keycaps don't.
/// </summary>
public static class GroupIcons
{
    public const string Default = "💳";
    private const int MaxLength = 32;

    public static readonly IReadOnlyList<string> QuickPicks =
    [
        "💳", "🏠", "🛒", "🍽️", "☕", "🍺", "✈️", "🏖️",
        "🚗", "⛽", "🚌", "💡", "📱", "🎉", "🎁", "🎮",
        "🎬", "🐶", "👶", "💊", "🎓", "🏋️", "🧾", "🔧",
    ];

    public static bool IsValid(string? value)
    {
        var icon = value?.Trim();
        if (string.IsNullOrEmpty(icon) || icon.Length > MaxLength) return false;
        if (new StringInfo(icon).LengthInTextElements != 1) return false;
        return Rune.GetUnicodeCategory(Rune.GetRuneAt(icon, 0)) == UnicodeCategory.OtherSymbol;
    }

    /// <summary>The trimmed icon; throws when it is not a single emoji.</summary>
    public static string Normalize(string? value) =>
        IsValid(value) ? value!.Trim() : throw new ArgumentException("The icon must be a single emoji.", nameof(value));

    /// <summary>What to show for a stored icon; groups saved before icons existed carry none.</summary>
    public static string Display(string? stored) => stored ?? Default;
}
