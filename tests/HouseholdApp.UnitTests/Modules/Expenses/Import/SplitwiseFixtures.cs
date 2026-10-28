namespace HouseholdApp.UnitTests.Modules.Expenses.Import;

internal static class SplitwiseFixtures
{
    public const string Casa = "casa-ficticia_2026-01-31_export.csv";
    public const string Viagem = "viagem-ficticia_2026-02-28_export.csv";

    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Modules", "Expenses", "Import", "Fixtures", name));
}
