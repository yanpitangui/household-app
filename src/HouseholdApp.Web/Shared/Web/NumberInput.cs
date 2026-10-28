using System.Globalization;

namespace HouseholdApp.Web.Shared.Web;

public static class NumberInput
{
    /// <summary>Value attribute for a number input: invariant "0.00", empty when not positive.</summary>
    public static string Value(decimal amount) =>
        amount > 0 ? amount.ToString("0.00", CultureInfo.InvariantCulture) : "";
}
