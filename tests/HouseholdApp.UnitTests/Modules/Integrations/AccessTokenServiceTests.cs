using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Identity;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class AccessTokenServiceTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Household = Guid.NewGuid();
    private readonly IAccessTokenRepository _repo = Substitute.For<IAccessTokenRepository>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeLogger<AccessTokenService> _logger = new();
    private static readonly string KeyBase64 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private readonly byte[] _key = Convert.FromBase64String(KeyBase64);

    private AccessTokenService CreateSut()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Id.Returns(Ana);
        return new AccessTokenService(_repo, currentUser, _time, _logger,
            new AccessTokenKey(_key));
    }

    private AccessTokenRecord Stored(string token, DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null) => new(
        Guid.NewGuid(), Ana, Household, "phone", AccessTokenFormat.Hash(token), AccessTokenFormat.Hint(token),
        [AccessTokenScopes.ListsAdd], null, null, _time.GetUtcNow(), expiresAt ?? _time.GetUtcNow().AddDays(90), null, revokedAt);

    [Test]
    public async Task Create_stores_only_the_hash_and_returns_the_token_once()
    {
        AccessTokenRecord? stored = null;
        await _repo.InsertAsync(Arg.Do<AccessTokenRecord>(r => stored = r), Arg.Any<CancellationToken>());

        var created = await CreateSut().CreateAsync(Household,
            new CreateAccessToken(" phone ", [AccessTokenScopes.ListsAdd], null, null, 90));

        await Assert.That(AccessTokenFormat.IsWellFormed(created.Token, _key)).IsTrue();
        await Assert.That(stored!.TokenHash.SequenceEqual(AccessTokenFormat.Hash(created.Token))).IsTrue();
        await Assert.That(stored.Name).IsEqualTo("phone");
        await Assert.That(stored.UserId).IsEqualTo(Ana);
        await Assert.That(stored.ExpiresAt).IsEqualTo(_time.GetUtcNow().AddDays(90));
    }

    [Test]
    [Arguments("", "lists:add", 90, "Errors.AccessToken.Name")]
    [Arguments("phone", "", 90, "Errors.AccessToken.Scopes")]
    [Arguments("phone", "admin:all", 90, "Errors.AccessToken.Scopes")]
    [Arguments("phone", "lists:add", 7, "Errors.AccessToken.Lifetime")]
    public async Task Create_rejects_invalid_requests(string name, string scope, int days, string key)
    {
        var scopes = scope.Length == 0 ? Array.Empty<string>() : [scope];

        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            CreateSut().CreateAsync(Household, new CreateAccessToken(name, scopes, null, null, days)));

        await Assert.That(ex!.Key).IsEqualTo(key);
    }

    [Test]
    public async Task Authenticate_accepts_a_live_token_and_touches_it()
    {
        var token = AccessTokenFormat.Generate(_key);
        var record = Stored(token);
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(record);

        var check = await CreateSut().AuthenticateAsync(token);

        await Assert.That(check.Status).IsEqualTo(AccessTokenStatus.Valid);
        await Assert.That(check.Token!.HouseholdId).IsEqualTo(Household);
        await _repo.Received(1).TouchAsync(record.Id, _time.GetUtcNow(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Authenticate_reports_expired_and_revoked_tokens()
    {
        var expired = AccessTokenFormat.Generate(_key);
        var revoked = AccessTokenFormat.Generate(_key);
        _repo.FindByHashAsync(Arg.Is<byte[]>(h => h.SequenceEqual(AccessTokenFormat.Hash(expired))), Arg.Any<CancellationToken>())
            .Returns(Stored(expired, expiresAt: _time.GetUtcNow()));
        _repo.FindByHashAsync(Arg.Is<byte[]>(h => h.SequenceEqual(AccessTokenFormat.Hash(revoked))), Arg.Any<CancellationToken>())
            .Returns(Stored(revoked, revokedAt: _time.GetUtcNow()));
        var sut = CreateSut();

        await Assert.That((await sut.AuthenticateAsync(expired)).Status).IsEqualTo(AccessTokenStatus.Expired);
        await Assert.That((await sut.AuthenticateAsync(revoked)).Status).IsEqualTo(AccessTokenStatus.Revoked);
        await _repo.DidNotReceiveWithAnyArgs().TouchAsync(default, default, default);
    }

    [Test]
    public async Task Authenticate_rejects_malformed_tokens_without_a_lookup()
    {
        var check = await CreateSut().AuthenticateAsync("hha_typo");

        await Assert.That(check.Status).IsEqualTo(AccessTokenStatus.Malformed);
        await _repo.DidNotReceiveWithAnyArgs().FindByHashAsync(default!, default);
    }

    [Test]
    public async Task The_raw_token_never_reaches_the_logs()
    {
        var unknown = AccessTokenFormat.Generate(_key);
        var sut = CreateSut();

        await sut.AuthenticateAsync(unknown);
        await sut.AuthenticateAsync(unknown[..20]);

        var records = _logger.Collector.GetSnapshot();
        await Assert.That(records.Count).IsGreaterThan(0);
        foreach (var record in records)
        {
            await Assert.That(record.Message).DoesNotContain(unknown[..20]);
            await Assert.That(record.StructuredState?.Any(kv => kv.Value?.Contains(unknown[..20]) == true) ?? false).IsFalse();
        }
    }

    [Test]
    public async Task Authenticate_still_succeeds_when_recording_last_use_fails()
    {
        var token = AccessTokenFormat.Generate(_key);
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(Stored(token));
        _repo.TouchAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("db down")));

        var check = await CreateSut().AuthenticateAsync(token);

        await Assert.That(check.Status).IsEqualTo(AccessTokenStatus.Valid);
    }

    [Test]
    public async Task A_token_both_revoked_and_expired_reports_revoked()
    {
        var token = AccessTokenFormat.Generate(_key);
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(Stored(token, expiresAt: _time.GetUtcNow().AddDays(-1), revokedAt: _time.GetUtcNow().AddDays(-2)));

        await Assert.That((await CreateSut().AuthenticateAsync(token)).Status).IsEqualTo(AccessTokenStatus.Revoked);
    }
}
