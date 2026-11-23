using HouseholdApp.Application.Modules.Integrations.Application.Operations;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class AccessTokenFormatTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

    [Test]
    public async Task Generate_produces_prefixed_fixed_length_well_formed_tokens()
    {
        var token = AccessTokenFormat.Generate(Key);

        await Assert.That(token.Length).IsEqualTo(55);
        await Assert.That(token).StartsWith("hha_");
        await Assert.That(AccessTokenFormat.IsWellFormed(token, Key)).IsTrue();
    }

    [Test]
    public async Task Generate_never_repeats()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => AccessTokenFormat.Generate(Key)).ToHashSet();

        await Assert.That(tokens.Count).IsEqualTo(1000);
    }

    [Test]
    public async Task IsWellFormed_rejects_a_single_changed_character()
    {
        var token = AccessTokenFormat.Generate(Key);
        var swapped = token[10] == 'A' ? 'B' : 'A';
        var typo = token[..10] + swapped + token[11..];

        await Assert.That(AccessTokenFormat.IsWellFormed(typo, Key)).IsFalse();
    }

    [Test]
    public async Task IsWellFormed_rejects_another_prefix_even_with_valid_length()
    {
        var token = AccessTokenFormat.Generate(Key);

        await Assert.That(AccessTokenFormat.IsWellFormed("ghp_" + token[4..], Key)).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("hha_short")]
    public async Task IsWellFormed_rejects_missing_or_short_input(string? token)
    {
        await Assert.That(AccessTokenFormat.IsWellFormed(token, Key)).IsFalse();
    }

    [Test]
    public async Task Hash_is_a_stable_sha256_per_token()
    {
        var token = AccessTokenFormat.Generate(Key);

        await Assert.That(AccessTokenFormat.Hash(token).Length).IsEqualTo(32);
        await Assert.That(AccessTokenFormat.Hash(token).SequenceEqual(AccessTokenFormat.Hash(token))).IsTrue();
        await Assert.That(AccessTokenFormat.Hash(token).SequenceEqual(AccessTokenFormat.Hash(AccessTokenFormat.Generate(Key)))).IsFalse();
    }

    [Test]
    public async Task Hint_is_the_last_four_random_characters()
    {
        var token = AccessTokenFormat.Generate(Key);

        await Assert.That(AccessTokenFormat.Hint(token)).IsEqualTo(token[43..47]);
    }

    [Test]
    public async Task A_token_made_with_another_key_is_not_well_formed()
    {
        var token = AccessTokenFormat.Generate(OtherKey);

        await Assert.That(AccessTokenFormat.IsWellFormed(token, OtherKey)).IsTrue();
        await Assert.That(AccessTokenFormat.IsWellFormed(token, Key)).IsFalse();
    }

    [Test]
    public async Task A_plain_sha256_checksum_is_rejected()
    {
        var body = AccessTokenFormat.Generate(Key)[..47];
        var plain = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(body)).AsSpan(0, 4));

        await Assert.That(AccessTokenFormat.IsWellFormed(body + plain, Key)).IsFalse();
    }

    [Test]
    public async Task A_tampered_checksum_character_is_rejected()
    {
        var token = AccessTokenFormat.Generate(Key);
        var tampered = token[..^1] + (token[^1] == '0' ? '1' : '0');

        await Assert.That(AccessTokenFormat.IsWellFormed(tampered, Key)).IsFalse();
    }

    [Test]
    public async Task An_uppercase_checksum_is_rejected()
    {
        var token = Enumerable.Range(0, 100).Select(_ => AccessTokenFormat.Generate(Key)).First(t => t[47..].Any(char.IsLetter));
        var upper = token[..47] + token[47..].ToUpperInvariant();

        await Assert.That(AccessTokenFormat.IsWellFormed(upper, Key)).IsFalse();
    }
}
