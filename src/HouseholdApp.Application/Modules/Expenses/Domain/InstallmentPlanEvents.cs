using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

// Plan events carry the balance facts of the Expenses they cover (CatchUpCount, payoff allocations, voided
// installments), so the activity row can show the viewer's delta from the plan stream alone.
public sealed record InstallmentPlanCreated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PlanId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    Guid PayerId,
    long TotalCents,
    long DownPaymentCents,
    int Count,
    DateOnly PurchaseDate,
    DateOnly FirstDate,
    IReadOnlyList<Allocation> Allocations,
    int CatchUpCount,
    Guid PerformedByUserId) : IDomainEvent;

public sealed record InstallmentPlanUpdated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PlanId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    Guid PayerId,
    SplitSegment Segment,
    string OldDescription,
    Guid PerformedByUserId) : IDomainEvent;

public sealed record InstallmentPlanPaidEarly(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PlanId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    Guid PayoffExpenseId,
    int FromNumber,
    int Count,
    long RemainingCents,
    long PayoffCents,
    Guid PayerId,
    IReadOnlyList<Allocation> Allocations,
    Guid PerformedByUserId) : IDomainEvent;

public sealed record VoidedInstallment(
    Guid ExpenseId, int Number, IReadOnlyList<FundingSource> FundingSources, IReadOnlyList<Allocation> Allocations);

public sealed record InstallmentPlanCancelled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PlanId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    IReadOnlyList<VoidedInstallment> VoidedInstallments,
    Guid PerformedByUserId) : IDomainEvent;

/// <summary>Bookkeeping only, so the sweep stops loading finished plans; the feed and pushes skip it.</summary>
public sealed record InstallmentPlanCompleted(
    Guid EventId, DateTimeOffset OccurredAt, Guid PlanId, Guid HouseholdId) : IDomainEvent;
