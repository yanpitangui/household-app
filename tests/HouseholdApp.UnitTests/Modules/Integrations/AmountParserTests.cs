using System.Globalization;
using HouseholdApp.Application.Modules.Integrations.Application.Operations;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class AmountParserTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    [Test]
    [Arguments("R$ 50,90", 5090L)]
    [Arguments("50 reais", 5000L)]
    [Arguments("1.234,56", 123456L)]
    [Arguments("pizza 7", 700L)]
    [Arguments("12,5 no mercado", 1250L)]
    public async Task Brazilian_amounts(string text, long cents)
    {
        var pick = AmountParser.Pick(AmountParser.FindAll(text, PtBr));

        await Assert.That(pick.Kind).IsEqualTo(MatchKind.Found);
        await Assert.That(pick.Hit!.Cents).IsEqualTo(cents);
    }

    [Test]
    [Arguments("$12.50 lunch", 1250L)]
    [Arguments("1,234.56", 123456L)]
    [Arguments("30 dollars", 3000L)]
    public async Task English_amounts(string text, long cents)
    {
        var pick = AmountParser.Pick(AmountParser.FindAll(text, En));

        await Assert.That(pick.Hit!.Cents).IsEqualTo(cents);
    }

    [Test]
    public async Task A_single_amount_with_currency_wins_over_bare_numbers()
    {
        var pick = AmountParser.Pick(AmountParser.FindAll("2 pizzas 50 reais", PtBr));

        await Assert.That(pick.Hit!.Cents).IsEqualTo(5000L);
        await Assert.That(pick.Hit.Raw).IsEqualTo("50 reais");
    }

    [Test]
    public async Task Several_bare_numbers_are_ambiguous()
    {
        var pick = AmountParser.Pick(AmountParser.FindAll("2 pizzas 50", PtBr));

        await Assert.That(pick.Kind).IsEqualTo(MatchKind.Ambiguous);
        await Assert.That(pick.Candidates).IsEquivalentTo(new[] { "2", "50" });
    }

    [Test]
    public async Task No_number_is_none()
    {
        await Assert.That(AmountParser.Pick(AmountParser.FindAll("pizza", PtBr)).Kind).IsEqualTo(MatchKind.None);
    }

    [Test]
    public async Task Hits_carry_their_position_for_removal()
    {
        var hit = AmountParser.FindAll("pizza R$ 50,90 hoje", PtBr).Single();

        await Assert.That(hit.Start).IsEqualTo(6);
        await Assert.That(hit.Length).IsEqualTo("R$ 50,90".Length);
    }

    [Test]
    [Arguments("R$ 12.50", "pt-BR")]
    [Arguments("gastei 12.50 reais", "pt-BR")]
    [Arguments("R$ 1,234", "pt-BR")]
    [Arguments("$12,50", "en")]
    [Arguments("1.000.000.000.000.000.000.000", "pt-BR")]
    [Arguments("R$ 2.000.000", "pt-BR")]
    public async Task Malformed_or_oversized_numbers_are_ambiguous(string text, string culture)
    {
        var pick = AmountParser.Pick(AmountParser.FindAll(text, CultureInfo.GetCultureInfo(culture)));

        await Assert.That(pick.Kind).IsEqualTo(MatchKind.Ambiguous);
        await Assert.That(pick.Hit).IsNull();
    }

    [Test]
    public async Task Singular_dollar_and_us_prefix_count_as_currency()
    {
        var br = AmountParser.Pick(AmountParser.FindAll("5 dólar", PtBr));
        var us = AmountParser.Pick(AmountParser.FindAll("US$5 coffee", En));

        await Assert.That(br.Hit!.Cents).IsEqualTo(500L);
        await Assert.That(br.Hit.HasCurrency).IsTrue();
        await Assert.That(us.Hit!.Cents).IsEqualTo(500L);
        await Assert.That(us.Hit.HasCurrency).IsTrue();
    }
}
