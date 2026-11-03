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
    HouseholdRole Role,
    // Everyone else in the household: their household list shows the new member count.
    IReadOnlyList<Guid> OtherMemberIds) : IDomainEvent;

public sealed record HouseholdMemberRemoved(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid HouseholdId,
    Guid UserId,
    // The members who stay: their household list shows the new member count.
    IReadOnlyList<Guid> OtherMemberIds) : IDomainEvent;

public sealed record HouseholdRoleChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid HouseholdId,
    Guid UserId,
    HouseholdRole NewRole) : IDomainEvent;

public sealed record HouseholdRenamed(
    Guid EventId, DateTimeOffset OccurredAt, Guid HouseholdId, string Name,
    IReadOnlyList<Guid> MemberIds) : IDomainEvent;

public sealed record HouseholdTimeZoneChanged(
    Guid EventId, DateTimeOffset OccurredAt, Guid HouseholdId, string TimeZone) : IDomainEvent;
