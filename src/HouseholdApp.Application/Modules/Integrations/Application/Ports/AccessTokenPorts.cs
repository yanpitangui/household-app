namespace HouseholdApp.Application.Modules.Integrations.Application.Ports;

public static class AccessTokenScopes
{
    public const string ListsAdd = "lists:add";
    public const string ExpensesRecord = "expenses:record";

    public static readonly IReadOnlyList<string> All = [ListsAdd, ExpensesRecord];
}

public sealed record AccessTokenRecord(
    Guid Id, Guid UserId, Guid HouseholdId, string Name, byte[] TokenHash, string TokenHint, string[] Scopes,
    Guid? DefaultListId, Guid? DefaultGroupId,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);

public interface IAccessTokenRepository
{
    Task InsertAsync(AccessTokenRecord token, CancellationToken ct = default);
    Task<AccessTokenRecord?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<AccessTokenRecord>> ListActiveAsync(Guid userId, Guid householdId, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid id, Guid userId, Guid householdId, DateTimeOffset now, CancellationToken ct = default);
    Task RevokeAllAsync(Guid userId, Guid householdId, DateTimeOffset now, CancellationToken ct = default);
    Task TouchAsync(Guid id, DateTimeOffset now, CancellationToken ct = default);
}

public sealed record CreateAccessToken(
    string Name, IReadOnlyCollection<string> Scopes, Guid? DefaultListId, Guid? DefaultGroupId, int LifetimeDays);

public sealed record CreatedAccessToken(Guid Id, string Token);

public sealed record AccessTokenSummary(
    Guid Id, string Name, string Hint, IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt, bool IsExpired);

public interface IAccessTokenCommands
{
    Task<CreatedAccessToken> CreateAsync(Guid householdId, CreateAccessToken request, CancellationToken ct = default);
    Task<IReadOnlyList<AccessTokenSummary>> ListAsync(Guid householdId, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid householdId, Guid tokenId, CancellationToken ct = default);
}

public enum AccessTokenStatus { Valid, Malformed, Unknown, Expired, Revoked }

public sealed record AuthenticatedToken(
    Guid Id, Guid UserId, Guid HouseholdId, IReadOnlyList<string> Scopes, Guid? DefaultListId, Guid? DefaultGroupId);

public sealed record AccessTokenCheck(AccessTokenStatus Status, AuthenticatedToken? Token);

public interface IAccessTokenAuthenticator
{
    Task<AccessTokenCheck> AuthenticateAsync(string token, CancellationToken ct = default);
}
