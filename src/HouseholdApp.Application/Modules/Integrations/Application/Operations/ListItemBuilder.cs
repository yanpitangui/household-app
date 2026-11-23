using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

public sealed record BuiltItems(IReadOnlyList<BuiltItem> Items, IReadOnlyList<string> Unparsed);

// Quantity and unit come from IngredientParser, the same pure parser recipe imports use.
public static class ListItemBuilder
{
    public static IReadOnlyList<string> SearchTerms(IReadOnlyList<ItemPiece> pieces)
    {
        var terms = new List<string>();
        for (var i = 0; i < pieces.Count; i++)
        {
            terms.Add(NameOf(pieces[i].Text));
            if (pieces[i].JoinerBefore is { } joiner) terms.Add(NameOf(Join(pieces[i - 1], joiner, pieces[i])));
        }
        return terms.Distinct().ToList();
    }

    public static BuiltItems Build(IReadOnlyList<ItemPiece> pieces, IReadOnlyDictionary<string, CatalogItemSuggestion> matches)
    {
        var raws = new List<string>();
        for (var i = 0; i < pieces.Count; i++)
        {
            if (i + 1 < pieces.Count && pieces[i + 1].JoinerBefore is { } joiner)
            {
                var joined = Join(pieces[i], joiner, pieces[i + 1]);
                var name = NameOf(joined);
                // "arroz e feijão" stays one item only when the catalog has that exact name; a merely similar
                // catalog hit would wrongly swallow two items.
                if (matches.TryGetValue(name, out var match)
                    && TextNormalizer.Normalize(match.Name) == TextNormalizer.Normalize(name))
                {
                    raws.Add(joined);
                    i++;
                    continue;
                }
            }
            raws.Add(pieces[i].Text);
        }

        var items = new List<BuiltItem>();
        var unparsed = new List<string>();
        foreach (var raw in raws)
        {
            var (quantity, unit, name) = IngredientParser.Parse(raw);
            // The parser returns the raw text as the name when only a quantity was found ("2 kg").
            if (quantity is not null && name == raw.Trim())
            {
                unparsed.Add(raw);
                continue;
            }
            items.Add(ToItem(name, quantity, unit, matches));
        }
        return new BuiltItems(items, unparsed);
    }

    public static BuiltItems FromItems(IReadOnlyList<ListItemInput> inputs, IReadOnlyDictionary<string, CatalogItemSuggestion> matches) =>
        new(inputs
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => ToItem(i.Name.Trim(), i.Quantity, i.Unit, matches))
            .ToList(), []);

    public static IReadOnlyList<string> SearchTerms(IReadOnlyList<ListItemInput> inputs) =>
        inputs.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Select(i => i.Name.Trim()).Distinct().ToList();

    private static BuiltItem ToItem(string name, string? quantity, string? unit, IReadOnlyDictionary<string, CatalogItemSuggestion> matches) =>
        matches.TryGetValue(name, out var match)
            ? new BuiltItem(match.Name, quantity, unit, match.Id, match.CategoryId)
            : new BuiltItem(name, quantity, unit, null, null);

    private static string NameOf(string raw) => IngredientParser.Parse(raw).Name;

    private static string Join(ItemPiece first, string joiner, ItemPiece second) => $"{first.Text} {joiner} {second.Text}";
}
