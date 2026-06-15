using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Households.Domain;

public sealed record HouseholdCreated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid HouseholdId,
    Guid OwnerId) : IDomainEvent;

public sealed record HouseholdMemberInvited(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid HouseholdId,
    Guid InvitationId) : IDomainEvent;

public sealed record HouseholdMemberJoined(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid HouseholdId,
    Guid UserId,
    HouseholdRole Role) : IDomainEvent;

public sealed record HouseholdMemberRemoved(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid HouseholdId,
    Guid UserId) : IDomainEvent;

public sealed record HouseholdRoleChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid HouseholdId,
    Guid UserId,
    HouseholdRole NewRole) : IDomainEvent;
