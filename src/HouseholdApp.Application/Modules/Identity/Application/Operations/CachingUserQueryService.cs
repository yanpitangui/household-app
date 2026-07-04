using HouseholdApp.Application.Modules.Identity.Application.Ports;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Identity.Application.Operations;

internal sealed class CachingUserQueryService(IUserQuery inner, IFusionCache cache) : IUserQuery
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public async Task<UserProfile?> GetBySubjectAsync(string subject, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<UserProfile?>(
            UserCacheKeys.BySubject(subject),
            token => inner.GetBySubjectAsync(subject, token),
            EntryOptions,
            token: ct);

    public async Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<UserProfile?>(
            UserCacheKeys.ById(id),
            token => inner.GetByIdAsync(id, token),
            EntryOptions,
            token: ct);

    public async Task<IReadOnlyList<UserProfile>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();
        var result = new List<UserProfile>();
        var missing = new List<Guid>();

        foreach (var id in idList)
        {
            var cached = await cache.TryGetAsync<UserProfile>(UserCacheKeys.ById(id), token: ct);
            if (cached.HasValue) result.Add(cached.Value);
            else missing.Add(id);
        }

        if (missing.Count > 0)
        {
            var fetched = await inner.GetByIdsAsync(missing, ct);
            foreach (var profile in fetched)
            {
                result.Add(profile);
                await cache.SetAsync(UserCacheKeys.ById(profile.Id), profile, EntryOptions, token: ct);
            }
        }

        return result;
    }
}
