using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class ListTextParserTests
{
    private static readonly NameCandidate Groceries = new(Guid.NewGuid(), "Groceries");
    private static readonly NameCandidate Pharmacy = new(Guid.NewGuid(), "Pharmacy");

    private static ListTextParse Parse(string? text, Guid? defaultList = null, string? list = null,
        IReadOnlyList<ListItemInput>? items = null) =>
        ListTextParser.Parse(new ListInput(text, items, list), new ListParseContext(defaultList, [Groceries, Pharmacy]));

    [Test]
    public async Task Splits_items_and_finds_the_named_list()
    {
        var result = Parse("add milk, eggs and 2 kg rice to groceries");

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.List).IsEqualTo(Groceries);
        await Assert.That(result.Parsed[IntakeFields.List].Source).IsEqualTo(ValueSource.Text);
        await Assert.That(result.Pieces.Select(p => p.Text)).IsEquivalentTo(new[] { "milk", "eggs", "2 kg rice" });
        await Assert.That(result.Pieces[1].JoinerBefore).IsNull();
        await Assert.That(result.Pieces[2].JoinerBefore).IsEqualTo("and");
    }

    [Test]
    public async Task Keeps_connectors_that_do_not_name_a_list()
    {
        var result = Parse("adiciona pão na chapa", defaultList: Groceries.Id);

        await Assert.That(result.List).IsEqualTo(Groceries);
        await Assert.That(result.Parsed[IntakeFields.List].Source).IsEqualTo(ValueSource.Default);
        await Assert.That(result.Pieces.Single().Text).IsEqualTo("pão na chapa");
    }

    [Test]
    public async Task A_named_list_that_does_not_exist_is_not_found()
    {
        var result = Parse("leite na lista da garagem");

        var issue = result.Issues.Single();
        await Assert.That(issue.Field).IsEqualTo(IntakeFields.List);
        await Assert.That(issue.Reason).IsEqualTo(IssueReason.NotFound);
        await Assert.That(issue.Candidates).IsEquivalentTo(new[] { "Groceries", "Pharmacy" });
        await Assert.That(result.Pieces.Single().Text).IsEqualTo("leite");
    }

    [Test]
    public async Task No_list_and_no_default_is_missing()
    {
        var result = Parse("milk");

        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.Missing);
    }

    [Test]
    public async Task Explicit_list_with_structured_items_skips_text()
    {
        var result = Parse(null, list: "pharmacy", items: [new ListItemInput("aspirin", null, null)]);

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.List).IsEqualTo(Pharmacy);
        await Assert.That(result.Parsed[IntakeFields.List].Source).IsEqualTo(ValueSource.Explicit);
        await Assert.That(result.Pieces).IsEmpty();
    }

    [Test]
    public async Task Text_with_nothing_left_is_missing_items()
    {
        var result = Parse("add to groceries");

        await Assert.That(result.Issues.Single().Field).IsEqualTo(IntakeFields.Items);
    }

    [Test]
    [Arguments("0,5 kg queijo, 2 pães", "0,5 kg queijo", "2 pães")]
    [Arguments("1,5 kg de arroz, leite", "1,5 kg de arroz", "leite")]
    public async Task Decimal_commas_do_not_split_items(string text, string first, string second)
    {
        var result = Parse(text, defaultList: Groceries.Id);

        await Assert.That(result.Pieces.Select(p => p.Text)).IsEquivalentTo(new[] { first, second });
    }

    [Test]
    public async Task Items_after_the_list_name_stay_items()
    {
        var market = new NameCandidate(Guid.NewGuid(), "Mercado");
        var result = ListTextParser.Parse(new ListInput("adiciona leite no mercado e pão", null, null),
            new ListParseContext(null, [market, new NameCandidate(Guid.NewGuid(), "Farmácia")]));

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.List).IsEqualTo(market);
        await Assert.That(result.Pieces.Select(p => p.Text)).IsEquivalentTo(new[] { "leite", "pão" });
    }

    [Test]
    public async Task Plain_to_list_sentence_still_works()
    {
        var result = Parse("milk to groceries");

        await Assert.That(result.List).IsEqualTo(Groceries);
        await Assert.That(result.Pieces.Single().Text).IsEqualTo("milk");
    }

    [Test]
    [Arguments("add milk to the groceries list", "milk")]
    [Arguments("leite na lista Groceries por favor", "leite")]
    public async Task Politeness_and_list_keyword_after_the_name_are_not_items(string text, string item)
    {
        var result = Parse(text);

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.List).IsEqualTo(Groceries);
        await Assert.That(result.Pieces.Select(p => p.Text)).IsEquivalentTo(new[] { item });
    }

    [Test]
    public async Task Words_after_the_list_name_that_are_not_items_make_it_not_a_list_phrase()
    {
        var result = Parse("milk to groceries today");

        await Assert.That(result.List).IsNull();
        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.Missing);
    }
}
