using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public sealed record ActivityFeedItem(
    Guid Id,
    DateTimeOffset OccurredAt,
    ActivityKind Kind,
    string ActorDisplayName,
    string Description,
    string? GroupName,
    string? PayerDisplayName,
    string? RecipientDisplayName,
    long? ViewerDeltaCents);

public sealed record ActivityFeedPage(
    IReadOnlyList<ActivityFeedItem> Items,
    string? NextCursor);

public interface IActivityFeedQueries
{
    Task<ActivityFeedPage> GetActivityFeedAsync(
        Guid householdId, Guid viewerId, string? cursor, int pageSize, CancellationToken ct = default);
}
