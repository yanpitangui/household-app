using HouseholdApp.Application.Shared.Caching;

namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public interface IHouseholdQueriesWithLastModified
{
    Task<WithLastModified<HouseholdDetail?>> GetWithLastModifiedAsync(Guid householdId, CancellationToken ct = default);
    Task<WithLastModified<IReadOnlyList<HouseholdSummary>>> ListForUserWithLastModifiedAsync(Guid userId, CancellationToken ct = default);
    Task<WithLastModified<IReadOnlyList<HouseholdName>>> ListNamesWithLastModifiedAsync(Guid userId, CancellationToken ct = default);
}
