using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public sealed record ExpenseListParticipantDto(Guid UserId, string DisplayName, long Cents, string? PictureUrl);

public sealed record ExpenseListItem(
    Guid Id,
    string Description,
    DateTimeOffset Date,
    long TotalCents,
    IReadOnlyList<ExpenseListParticipantDto> FundingSources,
    IReadOnlyList<ExpenseListParticipantDto> Allocations,
    string? GroupName,
    DateTimeOffset? RecordedAt = null);

public enum PaymentPerspective { ViewerPaid, ViewerReceived, Others }

/// <summary>A recorded payment between two members. Payer/Recipient Cents carry the payment amount.</summary>
public sealed record SettlementListItem(
    Guid Id,
    DateTimeOffset Date,
    ExpenseListParticipantDto Payer,
    ExpenseListParticipantDto Recipient,
    long Cents,
    string? GroupName,
    bool IsHouseholdSettle = false,
    bool IsOffset = false)
{
    public PaymentPerspective PerspectiveFor(Guid viewerId) =>
        Payer.UserId == viewerId ? PaymentPerspective.ViewerPaid
        : Recipient.UserId == viewerId ? PaymentPerspective.ViewerReceived
        : PaymentPerspective.Others;
}

public sealed record GroupCard(
    Guid Id, string Name, IReadOnlyList<MemberBalance> Balances,
    string? LastExpenseDescription, DateTimeOffset? LastExpenseDate);

/// <summary>
/// Expenses/Settlements hold the Recent timeline only (after the last settled-up marker, or the latest 30 without
/// one). <see cref="HasOlder"/> says more exist; <see cref="FirstOlderCursor"/> starts the Older paging where Recent
/// ended (null when the settled-up marker alone defines Older).
/// </summary>
public sealed record ExpensesHome(
    IReadOnlyList<MemberBalance> Balances, IReadOnlyList<GroupCard> Groups,
    IReadOnlyList<ExpenseListItem> Expenses, IReadOnlyList<SettlementListItem> Settlements,
    bool HasOlder = false, string? FirstOlderCursor = null);

public sealed record GroupPage(
    Guid Id, string Name, IReadOnlyList<MemberBalance> Balances,
    IReadOnlyList<ExpenseListItem> Expenses, IReadOnlyList<SettlementListItem> Settlements,
    bool HasOlder = false, string? FirstOlderCursor = null);

public sealed record TimelinePage(
    IReadOnlyList<ExpenseListItem> Expenses, IReadOnlyList<SettlementListItem> Settlements, string? NextCursor);

public sealed record SettlementPreviewLeg(Guid GroupId, string GroupName, long Cents, bool IsOffset);

public sealed record SettlementPreview(
    IReadOnlyList<SettlementPreviewLeg> Legs, long OwedCents, SettlementSplitError? Error);

public sealed record ExpenseDetail(
    Guid Id, Guid HouseholdId, Guid ExpenseGroupId, string Description, DateTimeOffset Date,
    long TotalCents, bool IsVoided, string? VoidReason,
    IReadOnlyList<FundingSourceDto> FundingSources,
    IReadOnlyList<AllocationDto> Allocations,
    DateTimeOffset RecordedAt);

/// <summary>
/// Balance between the viewer and one other member. Positive Cents: the member owes the viewer;
/// negative: the viewer owes the member.
/// </summary>
public sealed record MemberBalance(Guid UserId, string DisplayName, long Cents, string? PictureUrl);

public sealed record ExpenseGroupSummary(Guid Id, string Name, string? Description);

public sealed record RecurringExpenseSummary(
    Guid Id, string Description, RecurrenceFrequency Frequency, DateTimeOffset StartAt, bool IsActive, Guid ExpenseGroupId);

public interface IExpenseQueries
{
    /// <summary>Viewer-relative total and per-group balances over non-deleted groups; see <see cref="MemberBalance"/>.</summary>
    Task<ExpensesHome> GetExpensesHomeAsync(Guid householdId, Guid viewerId, CancellationToken ct = default);

    /// <summary>Null when the group is missing, belongs to another household, or is deleted.</summary>
    Task<GroupPage?> GetGroupPageAsync(Guid householdId, Guid groupId, Guid viewerId, CancellationToken ct = default);

    /// <summary>
    /// One keyset page of entries older than the Recent timeline of a group (or of the home when
    /// <paramref name="groupId"/> is null). A null <paramref name="cursor"/> starts where the settled-up marker ends Recent.
    /// </summary>
    Task<TimelinePage> GetOlderTimelineAsync(
        Guid householdId, Guid? groupId, string? cursor, int pageSize = 30, CancellationToken ct = default);

    /// <summary>Viewer-relative balances over non-deleted groups, or one group when <paramref name="groupId"/> is set.</summary>
    Task<IReadOnlyList<MemberBalance>> GetBalancesAsync(
        Guid householdId, Guid viewerId, Guid? groupId = null, CancellationToken ct = default);

    Task<SettlementPreview> PreviewHouseholdSettlementAsync(
        Guid householdId, Guid payerId, Guid recipientId, long cents, CancellationToken ct = default);

    Task<ExpenseDetail?> GetExpenseAsync(Guid expenseId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseGroupSummary>> ListExpenseGroupsAsync(
        Guid householdId, CancellationToken ct = default);

    Task<IReadOnlyList<RecurringExpenseSummary>> ListRecurringExpensesAsync(
        Guid householdId, CancellationToken ct = default);
}
