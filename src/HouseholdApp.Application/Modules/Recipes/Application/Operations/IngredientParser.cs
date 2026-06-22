using System.Text.RegularExpressions;

namespace HouseholdApp.Application.Modules.Recipes.Application.Operations;

public static class IngredientParser
{
    private static readonly Regex LeadingNumber = new(
        @"^(\d+(?:[.,]\d+)?(?:\s*/\s*\d+)?)\s*", RegexOptions.Compiled);

    // Multi-word units must come before their single-word prefixes.
    private static readonly string[] MultiWordUnits =
    [
        "colheres de sopa", "colher de sopa",
        "colheres de chá",  "colher de chá",
        "xícaras de chá",   "xícara de chá",
        "fluid ounces",     "fluid ounce",  "fl oz",
    ];

    private static readonly HashSet<string> SingleWordUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        // pt-BR
        "xícara", "xícaras", "copo", "copos",
        "colher", "colheres",
        "kg", "g", "mg", "ml", "l", "litro", "litros",
        "lata", "latas", "unidade", "unidades",
        "dente", "dentes", "fatia", "fatias",
        "pedaço", "pedaços", "pitada", "pitadas",
        "maço", "maços", "ramo", "ramos", "cabeça", "cabeças",
        "sachê", "sachês", "dose", "doses",
        "grão", "grãos", "folha", "folhas",
        "fio", "fios", "pacote", "pacotes",
        // en
        "tablespoon", "tablespoons", "tbsp",
        "teaspoon",   "teaspoons",   "tsp",
        "cup",  "cups",
        "oz",   "lb",  "lbs",   "pound",  "pounds",
        "ounce", "ounces",
        "liter", "liters", "litre", "litres",
        "clove",  "cloves",
        "slice",  "slices",
        "piece",  "pieces",
        "pinch",  "bunch",
        "can",    "cans",
        "package", "packages",
        "stick",  "sticks",
        "head",   "heads",
        "stalk",  "stalks",
        "sprig",  "sprigs",
        "quart",  "quarts",
        "pint",   "pints",
        "gallon", "gallons",
    };

    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
        { "de", "of" };

    /// <summary>
    /// Parses a raw ingredient string into (quantity, unit, name).
    /// "4 xícaras de farinha" → ("4", "xícaras", "farinha")
    /// "4 batatas médias"     → ("4", null, "batatas médias")
    /// "sal a gosto"          → (null, null, "sal a gosto")
    /// </summary>
    public static (string? Quantity, string? Unit, string Name) Parse(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrEmpty(raw)) return (null, null, raw);

        var qtyMatch = LeadingNumber.Match(raw);
        if (!qtyMatch.Success) return (null, null, raw);

        var qty = qtyMatch.Groups[1].Value.Trim();
        var rest = raw[qtyMatch.Length..].Trim();

        if (string.IsNullOrEmpty(rest)) return (qty, null, raw);

        // Try multi-word units first
        string? unit = null;
        foreach (var mu in MultiWordUnits)
        {
            if (rest.StartsWith(mu, StringComparison.OrdinalIgnoreCase))
            {
                unit = mu;
                rest = rest[mu.Length..].TrimStart();
                break;
            }
        }

        // Try single-word unit
        if (unit is null)
        {
            var space = rest.IndexOf(' ');
            var firstWord = space < 0 ? rest : rest[..space];
            if (SingleWordUnits.Contains(firstWord))
            {
                unit = firstWord;
                rest = space < 0 ? "" : rest[(space + 1)..].TrimStart();
            }
        }

        // Skip connector ("de" / "of") between unit and name
        if (unit is not null && !string.IsNullOrEmpty(rest))
        {
            var space = rest.IndexOf(' ');
            var firstWord = space < 0 ? rest : rest[..space];
            if (Connectors.Contains(firstWord))
                rest = space < 0 ? "" : rest[(space + 1)..].TrimStart();
        }

        var name = string.IsNullOrEmpty(rest) ? raw : rest;
        return (qty, unit, name);
    }
}
