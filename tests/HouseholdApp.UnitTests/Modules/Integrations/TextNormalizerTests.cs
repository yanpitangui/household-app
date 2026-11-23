using HouseholdApp.Application.Modules.Integrations.Application.Operations;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class TextNormalizerTests
{
    [Test]
    [Arguments("Despesas Variáveis", "despesa variavel")]
    [Arguments("papéis", "papel")]
    [Arguments("animais", "animal")]
    [Arguments("limões", "limao")]
    [Arguments("pães", "pao")]
    [Arguments("  Café,  da Manhã! ", "cafe da manha")]
    [Arguments("Expenses", "expense")]
    [Arguments("gas", "gas")]
    [Arguments("R$50", "r 50")]
    public async Task Normalize_lowercases_strips_accents_punctuation_and_plural_s(string input, string expected)
    {
        await Assert.That(TextNormalizer.Normalize(input)).IsEqualTo(expected);
    }

    [Test]
    public async Task Tokenize_keeps_original_positions()
    {
        var tokens = TextTokens.Tokenize("pizza na Viagem");

        await Assert.That(tokens.Select(t => t.Norm)).IsEquivalentTo(new[] { "pizza", "na", "viagem" });
        await Assert.That(tokens[2].Start).IsEqualTo(9);
        await Assert.That(tokens[2].Length).IsEqualTo(6);
    }

    [Test]
    public async Task Tokenize_handles_decomposed_accents()
    {
        var tokens = TextTokens.Tokenize("cafe\u0301 na viagem");

        await Assert.That(tokens.Select(t => t.Norm)).IsEquivalentTo(new[] { "cafe", "na", "viagem" });
    }
}
