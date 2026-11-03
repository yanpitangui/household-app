using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Domain;

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
    long? DownPaymentCents = null,
    Guid? RecurringExpenseId = null,
    RecurrenceFrequency? Frequency = null,
    Guid? ExpenseId = null,
    string? GroupIcon = null,
    IReadOnlyList<ActivityGroupRef>? SettledGroups = null);

/// <summary>A group named on a household settlement row; Id is null when the group is deleted, so it renders unlinked.</summary>
public sealed record ActivityGroupRef(Guid? Id, string Name, string Icon);

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

    /// <summary>Every activity row of one recurring template, newest first.</summary>
    Task<IReadOnlyList<ActivityFeedItem>> GetRecurringHistoryAsync(
        Guid householdId, Guid recurringExpenseId, Guid viewerId, CancellationToken ct = default);
}
