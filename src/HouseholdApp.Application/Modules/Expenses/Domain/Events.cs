using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

public sealed record FundingSource(Guid UserId, long Cents);
public sealed record Allocation(Guid UserId, long Cents);

public sealed record ExpenseGroupCreated(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid GroupId, Guid HouseholdId, string Name) : IDomainEvent;

public sealed record ExpenseRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ExpenseId,
    Guid HouseholdId,
    Guid ExpenseGroupId,
    string Description,
    DateTimeOffset Date,
    IReadOnlyList<FundingSource> FundingSources,
    IReadOnlyList<Allocation> Allocations) : IDomainEvent;

public sealed record ExpenseVoided(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ExpenseId,
    Guid HouseholdId,
    string? Reason) : IDomainEvent;

public sealed record SettlementRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid SettlementId,
    Guid HouseholdId,
    Guid PayerId,
    Guid RecipientId,
    long Cents,
    DateTimeOffset Date) : IDomainEvent;

public sealed record ExpenseGroupDeleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid GroupId, Guid HouseholdId) : IDomainEvent;
