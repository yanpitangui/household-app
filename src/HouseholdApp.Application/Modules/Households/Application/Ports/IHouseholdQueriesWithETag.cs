using HouseholdApp.Application.Shared.Caching;

namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public interface IHouseholdQueriesWithETag
{
    Task<WithETag<HouseholdDetail?>> GetWithETagAsync(Guid householdId, CancellationToken ct = default);
    Task<WithETag<IReadOnlyList<HouseholdSummary>>> ListForUserWithETagAsync(Guid userId, CancellationToken ct = default);
}
