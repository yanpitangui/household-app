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
    string? GroupName);

public enum PaymentPerspective { ViewerPaid, ViewerReceived, Others }

/// <summary>A recorded payment between two members. Payer/Recipient Cents carry the payment amount.</summary>
public sealed record SettlementListItem(
    Guid Id,
    DateTimeOffset Date,
    ExpenseListParticipantDto Payer,
    ExpenseListParticipantDto Recipient,
    long Cents,
    string? GroupName)
{
    public PaymentPerspective PerspectiveFor(Guid viewerId) =>
        Payer.UserId == viewerId ? PaymentPerspective.ViewerPaid
        : Recipient.UserId == viewerId ? PaymentPerspective.ViewerReceived
        : PaymentPerspective.Others;
}

public sealed record ExpensesSummary(
    IReadOnlyList<ExpenseListItem> Expenses,
    IReadOnlyList<SettlementListItem> Settlements,
    IReadOnlyList<MemberBalance> Balances);

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
    /// <summary>Balances are relative to <paramref name="viewerId"/>; see <see cref="MemberBalance"/>.</summary>
    Task<ExpensesSummary> GetExpensesSummaryAsync(
        Guid householdId, Guid viewerId, Guid? groupId = null, CancellationToken ct = default);

    /// <summary>Viewer-relative balances only; see <see cref="MemberBalance"/>.</summary>
    Task<IReadOnlyList<MemberBalance>> GetBalancesAsync(
        Guid householdId, Guid viewerId, CancellationToken ct = default);

    Task<ExpenseDetail?> GetExpenseAsync(Guid expenseId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseGroupSummary>> ListExpenseGroupsAsync(
        Guid householdId, CancellationToken ct = default);

    Task<IReadOnlyList<RecurringExpenseSummary>> ListRecurringExpensesAsync(
        Guid householdId, CancellationToken ct = default);
}
