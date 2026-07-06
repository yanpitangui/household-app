using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Tasks.Domain;

public sealed record TaskCreated(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid TaskId, Guid HouseholdId, string Title,
    Guid? AssignedTo, DateTimeOffset? DueDate,
    Guid? RecurringTaskId) : IDomainEvent;

public sealed record TaskAssigned(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid TaskId, Guid HouseholdId, Guid AssignedTo) : IDomainEvent;

public sealed record TaskCompleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid TaskId, Guid HouseholdId, Guid CompletedBy) : IDomainEvent;

public sealed record TaskUncompleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid TaskId, Guid HouseholdId) : IDomainEvent;

public sealed record TaskDeleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid TaskId, Guid HouseholdId, Guid DeletedBy) : IDomainEvent;
