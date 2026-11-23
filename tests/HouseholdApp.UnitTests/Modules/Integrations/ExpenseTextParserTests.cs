using System.Globalization;
using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class ExpenseTextParserTests
{
    private static readonly NameCandidate Ana = new(Guid.NewGuid(), "Ana Souza");
    private static readonly NameCandidate Leo = new(Guid.NewGuid(), "Leo Lima");
    private static readonly NameCandidate Variable = new(Guid.NewGuid(), "Despesas Variáveis");
    private static readonly NameCandidate Trip = new(Guid.NewGuid(), "Viagem");

    private static ExpenseParseContext Ctx(Guid? defaultGroup = null, string culture = "pt-BR") =>
        new(Ana.Id, defaultGroup, [Ana, Leo], [Variable, Trip], CultureInfo.GetCultureInfo(culture));

    private static ExpenseParse Parse(string? text, ExpenseParseContext ctx,
        string? amount = null, string? description = null, string? group = null, string? payer = null) =>
        ExpenseTextParser.Parse(new ExpenseInput(text, amount, description, group, payer), ctx);

    [Test]
    public async Task Reads_amount_group_and_description_with_the_owner_as_payer()
    {
        var result = Parse("50 reais mercado em despesas variaveis", Ctx());

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.AmountCents).IsEqualTo(5000L);
        await Assert.That(result.Description).IsEqualTo("mercado");
        await Assert.That(result.Group).IsEqualTo(Variable);
        await Assert.That(result.Payer).IsEqualTo(Ana);
        await Assert.That(result.Parsed[IntakeFields.Payer].Source).IsEqualTo(ValueSource.Default);
        await Assert.That(result.Parsed[IntakeFields.Group].Source).IsEqualTo(ValueSource.Text);
    }

    [Test]
    public async Task Reads_paid_by_and_falls_back_to_the_default_group()
    {
        var result = Parse("R$ 12,50 farmácia pago por Leo", Ctx(defaultGroup: Trip.Id));

        await Assert.That(result.AmountCents).IsEqualTo(1250L);
        await Assert.That(result.Payer).IsEqualTo(Leo);
        await Assert.That(result.Group).IsEqualTo(Trip);
        await Assert.That(result.Parsed[IntakeFields.Group].Source).IsEqualTo(ValueSource.Default);
        await Assert.That(result.Description).IsEqualTo("farmácia");
    }

    [Test]
    public async Task Reads_name_paid_before_the_verb()
    {
        var result = Parse("Leo pagou 30 pizza na viagem", Ctx());

        await Assert.That(result.Payer).IsEqualTo(Leo);
        await Assert.That(result.Group).IsEqualTo(Trip);
        await Assert.That(result.Description).IsEqualTo("pizza");
    }

    [Test]
    public async Task Reads_english_sentences()
    {
        var result = Parse("$12.50 lunch paid by Ana in viagem", Ctx(culture: "en"));

        await Assert.That(result.AmountCents).IsEqualTo(1250L);
        await Assert.That(result.Payer).IsEqualTo(Ana);
        await Assert.That(result.Description).IsEqualTo("lunch");
    }

    [Test]
    public async Task Keeps_bare_numbers_in_the_description_when_one_amount_is_marked()
    {
        var result = Parse("2 pizzas 50 reais na viagem", Ctx());

        await Assert.That(result.AmountCents).IsEqualTo(5000L);
        await Assert.That(result.Description).IsEqualTo("2 pizzas");
    }

    [Test]
    public async Task Several_bare_numbers_are_an_ambiguous_amount()
    {
        var result = Parse("2 pizzas 50 na viagem", Ctx());

        var issue = result.Issues.Single();
        await Assert.That(issue.Field).IsEqualTo(IntakeFields.Amount);
        await Assert.That(issue.Reason).IsEqualTo(IssueReason.Ambiguous);
        await Assert.That(issue.Candidates).IsEquivalentTo(new[] { "2", "50" });
    }

    [Test]
    public async Task Reports_every_missing_piece()
    {
        var result = Parse("50", Ctx());

        await Assert.That(result.Issues.Select(i => i.Field)).IsEquivalentTo(new[] { IntakeFields.Group, IntakeFields.Description });
        await Assert.That(result.Issues.Single(i => i.Field == IntakeFields.Group).Candidates)
            .IsEquivalentTo(new[] { "Despesas Variáveis", "Viagem" });
    }

    [Test]
    public async Task Explicit_fields_win_and_remove_the_echoed_amount_from_the_text()
    {
        var result = Parse("2 pizzas 50", Ctx(), amount: "50", group: "viagem");

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.AmountCents).IsEqualTo(5000L);
        await Assert.That(result.Parsed[IntakeFields.Amount].Source).IsEqualTo(ValueSource.Explicit);
        await Assert.That(result.Group).IsEqualTo(Trip);
        await Assert.That(result.Description).IsEqualTo("2 pizzas");
    }

    [Test]
    public async Task Explicit_description_reports_the_leftover_text_as_unparsed()
    {
        var result = Parse("50 algo estranho na viagem", Ctx(), description: "pizza");

        await Assert.That(result.Description).IsEqualTo("pizza");
        await Assert.That(result.Unparsed).IsEquivalentTo(new[] { "algo estranho" });
    }

    [Test]
    public async Task Unknown_payer_lists_the_members()
    {
        var result = Parse("50 pizza pago por Zé na viagem", Ctx());

        var issue = result.Issues.Single();
        await Assert.That(issue.Field).IsEqualTo(IntakeFields.Payer);
        await Assert.That(issue.Reason).IsEqualTo(IssueReason.NotFound);
        await Assert.That(issue.Candidates).IsEquivalentTo(new[] { "Ana Souza", "Leo Lima" });
    }

    [Test]
    public async Task Paid_by_me_is_the_owner()
    {
        var result = Parse("50 pizza pago por mim na viagem", Ctx());

        await Assert.That(result.Payer).IsEqualTo(Ana);
        await Assert.That(result.Description).IsEqualTo("pizza");
    }

    [Test]
    public async Task Paid_by_a_lowercase_word_that_is_not_a_member_is_ignored()
    {
        var result = Parse("50 pizza paid by card na viagem", Ctx());

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.Payer).IsEqualTo(Ana);
        await Assert.That(result.Parsed[IntakeFields.Payer].Source).IsEqualTo(ValueSource.Default);
    }

    [Test]
    public async Task Payment_method_phrase_is_consumed_and_the_owner_pays()
    {
        var result = Parse("50 pizza paid by card na viagem", Ctx());

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.Payer).IsEqualTo(Ana);
        await Assert.That(result.Description).IsEqualTo("pizza");
    }

    [Test]
    public async Task A_lowercase_unknown_payer_is_not_found()
    {
        var result = Parse("pago pelo joao 40 jantar na viagem", Ctx());

        await Assert.That(result.Issues.Single().Field).IsEqualTo(IntakeFields.Payer);
        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.NotFound);
    }
}
