using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class ListItemBuilderTests
{
    private static CatalogItemSuggestion Item(string name) => new(Guid.NewGuid(), name, Guid.NewGuid(), "Cat", "🛒", null);

    private static readonly IReadOnlyList<ItemPiece> RiceAndBeans = [new("arroz", null), new("feijão", "e")];

    [Test]
    public async Task SearchTerms_include_each_piece_and_each_joined_pair()
    {
        var terms = ListItemBuilder.SearchTerms(RiceAndBeans);

        await Assert.That(terms).IsEquivalentTo(new[] { "arroz", "feijão", "arroz e feijão" });
    }

    [Test]
    public async Task Joins_pieces_when_the_catalog_knows_the_exact_combined_name()
    {
        var combined = Item("Arroz e Feijão");
        var matches = new Dictionary<string, CatalogItemSuggestion> { ["arroz e feijão"] = combined };

        var built = ListItemBuilder.Build(RiceAndBeans, matches);

        await Assert.That(built.Items.Single().Name).IsEqualTo("Arroz e Feijão");
        await Assert.That(built.Items.Single().CatalogItemId).IsEqualTo(combined.Id);
    }

    [Test]
    public async Task Keeps_pieces_apart_when_the_catalog_match_is_only_similar()
    {
        var matches = new Dictionary<string, CatalogItemSuggestion> { ["arroz e feijão"] = Item("Arroz") };

        var built = ListItemBuilder.Build(RiceAndBeans, matches);

        await Assert.That(built.Items.Select(i => i.Name)).IsEquivalentTo(new[] { "arroz", "feijão" });
    }

    [Test]
    public async Task Reads_quantity_and_unit_and_uses_catalog_matches()
    {
        var rice = Item("Arroz");
        var matches = new Dictionary<string, CatalogItemSuggestion> { ["arroz"] = rice };

        var built = ListItemBuilder.Build([new ItemPiece("2 kg de arroz", null), new ItemPiece("3 eggs", null)], matches);

        await Assert.That(built.Items[0]).IsEqualTo(new BuiltItem("Arroz", "2", "kg", rice.Id, rice.CategoryId));
        await Assert.That(built.Items[1]).IsEqualTo(new BuiltItem("eggs", "3", null, null, null));
    }

    [Test]
    public async Task A_quantity_without_a_name_is_unparsed()
    {
        var built = ListItemBuilder.Build([new ItemPiece("milk", null), new ItemPiece("2 kg", null)], new Dictionary<string, CatalogItemSuggestion>());

        await Assert.That(built.Items.Single().Name).IsEqualTo("milk");
        await Assert.That(built.Unparsed).IsEquivalentTo(new[] { "2 kg" });
    }

    [Test]
    public async Task FromItems_keeps_given_fields_and_matches_the_catalog()
    {
        var milk = Item("Leite");
        var matches = new Dictionary<string, CatalogItemSuggestion> { ["leite"] = milk };

        var built = ListItemBuilder.FromItems([new ListItemInput(" leite ", "2", "l"), new ListItemInput(" ", null, null)], matches);

        await Assert.That(built.Items.Single()).IsEqualTo(new BuiltItem("Leite", "2", "l", milk.Id, milk.CategoryId));
    }
}
