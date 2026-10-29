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
    long? ViewerDeltaCents,
    Guid? GroupId = null,
    IReadOnlyList<string>? GroupNames = null,
    long? SettlementCents = null,
    bool CanRestore = false,
    string? RenamedFrom = null,
    string? RenamedTo = null,
    Guid? InstallmentPlanId = null,
    int? InstallmentNumber = null,
    int? InstallmentCount = null,
    long? AmountCents = null,
    long? ReferenceCents = null,
    int? VoidedCount = null,
    long? DownPaymentCents = null);

public sealed record ActivityFeedPage(
    IReadOnlyList<ActivityFeedItem> Items,
    string? NextCursor);

public interface IActivityFeedQueries
{
    Task<ActivityFeedPage> GetActivityFeedAsync(
        Guid householdId, Guid viewerId, string? cursor, int pageSize, CancellationToken ct = default);

    /// <summary>Every activity row of one Installment Plan, newest first.</summary>
    Task<IReadOnlyList<ActivityFeedItem>> GetPlanHistoryAsync(
        Guid householdId, Guid planId, Guid viewerId, CancellationToken ct = default);
}
