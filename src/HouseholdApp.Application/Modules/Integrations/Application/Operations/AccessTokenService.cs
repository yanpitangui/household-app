using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Identity;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

public sealed class AccessTokenService(
    IAccessTokenRepository repo, ICurrentUser currentUser, TimeProvider time, ILogger<AccessTokenService> logger,
    AccessTokenKey tokenKey)
    : IAccessTokenCommands, IAccessTokenAuthenticator
{
    private readonly byte[] _key = tokenKey.Bytes;

    public static readonly IReadOnlyList<int> Lifetimes = [30, 90, 365];

    public async Task<CreatedAccessToken> CreateAsync(Guid householdId, CreateAccessToken request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (name.Length is 0 or > 100)
            throw new DomainRuleException("Errors.AccessToken.Name", "Access token name must have 1 to 100 characters.");
        var scopes = request.Scopes.Distinct().ToArray();
        if (scopes.Length == 0 || scopes.Any(s => !AccessTokenScopes.All.Contains(s)))
            throw new DomainRuleException("Errors.AccessToken.Scopes", "Pick at least one known permission.");
        if (!Lifetimes.Contains(request.LifetimeDays))
            throw new DomainRuleException("Errors.AccessToken.Lifetime", "Unsupported access token lifetime.");

        var token = AccessTokenFormat.Generate(_key);
        var now = time.GetUtcNow();
        var record = new AccessTokenRecord(
            Guid.CreateVersion7(), currentUser.Id, householdId, name,
            AccessTokenFormat.Hash(token), AccessTokenFormat.Hint(token), scopes,
            request.DefaultListId, request.DefaultGroupId, now, now.AddDays(request.LifetimeDays), null, null);
        await repo.InsertAsync(record, ct);
        return new CreatedAccessToken(record.Id, token);
    }

    public async Task<IReadOnlyList<AccessTokenSummary>> ListAsync(Guid householdId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var tokens = await repo.ListActiveAsync(currentUser.Id, householdId, ct);
        return tokens
            .Select(t => new AccessTokenSummary(t.Id, t.Name, t.TokenHint, t.Scopes, t.CreatedAt, t.ExpiresAt, t.LastUsedAt, t.ExpiresAt <= now))
            .ToList();
    }

    public Task<bool> RevokeAsync(Guid householdId, Guid tokenId, CancellationToken ct = default) =>
        repo.RevokeAsync(tokenId, currentUser.Id, householdId, time.GetUtcNow(), ct);

    // The raw token stays in this method: only its shape check and its hash leave it.
    public async Task<AccessTokenCheck> AuthenticateAsync(string token, CancellationToken ct = default)
    {
        if (!AccessTokenFormat.IsWellFormed(token, _key)) return Reject(AccessTokenStatus.Malformed);
        var record = await repo.FindByHashAsync(AccessTokenFormat.Hash(token), ct);
        if (record is null) return Reject(AccessTokenStatus.Unknown);
        if (record.RevokedAt is not null) return Reject(AccessTokenStatus.Revoked);
        var now = time.GetUtcNow();
        if (record.ExpiresAt <= now) return Reject(AccessTokenStatus.Expired);

        // Last-use bookkeeping is best effort; a failed write leaves the valid token usable.
        try
        {
            await repo.TouchAsync(record.Id, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            IntegrationsLog.TouchFailed(logger, record.Id, ex);
        }
        return new AccessTokenCheck(AccessTokenStatus.Valid, new AuthenticatedToken(
            record.Id, record.UserId, record.HouseholdId, record.Scopes, record.DefaultListId, record.DefaultGroupId));
    }

    private AccessTokenCheck Reject(AccessTokenStatus status)
    {
        IntegrationsLog.TokenRejected(logger, status);
        return new AccessTokenCheck(status, null);
    }
}
