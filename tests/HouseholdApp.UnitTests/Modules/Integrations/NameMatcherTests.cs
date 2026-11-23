using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class NameMatcherTests
{
    private static readonly NameCandidate General = new(Guid.NewGuid(), "General");
    private static readonly NameCandidate Variable = new(Guid.NewGuid(), "Despesas Variáveis");
    private static readonly NameCandidate Beach = new(Guid.NewGuid(), "Viagem Praia");
    private static readonly NameCandidate Mountain = new(Guid.NewGuid(), "Viagem Serra");
    private static readonly IReadOnlyList<NameCandidate> Groups = [General, Variable, Beach, Mountain];

    [Test]
    [Arguments("despesas variaveis")]
    [Arguments("DESPESA VARIÁVEL")]
    public async Task Match_ignores_case_accents_and_plurals(string phrase)
    {
        var match = NameMatcher.Match(phrase, Groups, allowOverlap: false);

        await Assert.That(match.Kind).IsEqualTo(MatchKind.Found);
        await Assert.That(match.Single).IsEqualTo(Variable);
    }

    [Test]
    public async Task Match_accepts_a_phrase_contained_in_one_name()
    {
        var match = NameMatcher.Match("praia", Groups);

        await Assert.That(match.Kind).IsEqualTo(MatchKind.Found);
        await Assert.That(match.Single).IsEqualTo(Beach);
    }

    [Test]
    public async Task Match_reports_every_tied_candidate()
    {
        var match = NameMatcher.Match("viagem", Groups);

        await Assert.That(match.Kind).IsEqualTo(MatchKind.Ambiguous);
        await Assert.That(match.Candidates).IsEquivalentTo(new[] { Beach, Mountain });
    }

    [Test]
    public async Task Match_falls_back_to_shared_words()
    {
        var match = NameMatcher.Match("variaveis da casa", Groups);

        await Assert.That(match.Kind).IsEqualTo(MatchKind.Found);
        await Assert.That(match.Single).IsEqualTo(Variable);
    }

    [Test]
    public async Task Match_skips_shared_words_when_overlap_is_off()
    {
        var match = NameMatcher.Match("variaveis da casa", Groups, allowOverlap: false);

        await Assert.That(match.Kind).IsEqualTo(MatchKind.None);
    }

    [Test]
    [Arguments("de")]
    [Arguments("")]
    [Arguments("mercado")]
    public async Task Match_finds_nothing_for_connectors_empty_or_unknown_phrases(string phrase)
    {
        await Assert.That(NameMatcher.Match(phrase, Groups).Kind).IsEqualTo(MatchKind.None);
    }

    [Test]
    public async Task FindIn_returns_the_name_and_its_token_span()
    {
        var tokens = TextTokens.Tokenize("pizza na viagem praia ontem");

        var span = NameMatcher.FindIn(tokens, Groups);

        await Assert.That(span.Match.Single).IsEqualTo(Beach);
        await Assert.That(span.TokenStart).IsEqualTo(2);
        await Assert.That(span.TokenCount).IsEqualTo(2);
    }

    [Test]
    public async Task FindIn_prefers_the_longest_name()
    {
        var house = new NameCandidate(Guid.NewGuid(), "Casa");
        var beachHouse = new NameCandidate(Guid.NewGuid(), "Casa Praia");

        var span = NameMatcher.FindIn(TextTokens.Tokenize("conta casa praia"), [house, beachHouse]);

        await Assert.That(span.Match.Single).IsEqualTo(beachHouse);
    }

    [Test]
    public async Task FindIn_needs_the_whole_name()
    {
        var span = NameMatcher.FindIn(TextTokens.Tokenize("pizza na viagem"), Groups);

        await Assert.That(span.Match.Kind).IsEqualTo(MatchKind.None);
    }

    [Test]
    public async Task Match_prefers_the_longest_name_contained_in_the_phrase()
    {
        var market = new NameCandidate(Guid.NewGuid(), "Mercado");
        var central = new NameCandidate(Guid.NewGuid(), "Mercado Central");

        var match = NameMatcher.Match("mercado central grande", [market, central]);

        await Assert.That(match.Kind).IsEqualTo(MatchKind.Found);
        await Assert.That(match.Single).IsEqualTo(central);
    }

    [Test]
    public async Task Match_finds_nothing_for_a_connector_only_phrase()
    {
        var mother = new NameCandidate(Guid.NewGuid(), "Mãe da Ana");
        var father = new NameCandidate(Guid.NewGuid(), "Pai da Ana");

        await Assert.That(NameMatcher.Match("da", [mother, father]).Kind).IsEqualTo(MatchKind.None);
    }

    [Test]
    public async Task FindIn_reports_equally_long_names_as_ambiguous()
    {
        var beach = new NameCandidate(Guid.NewGuid(), "Casa Praia");
        var mountain = new NameCandidate(Guid.NewGuid(), "Casa Serra");

        var span = NameMatcher.FindIn(TextTokens.Tokenize("casa praia casa serra"), [beach, mountain]);

        await Assert.That(span.Match.Kind).IsEqualTo(MatchKind.Ambiguous);
    }
}
