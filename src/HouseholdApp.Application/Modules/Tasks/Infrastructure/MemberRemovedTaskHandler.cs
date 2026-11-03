using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Tasks.Infrastructure;

/// <summary>Runs in the removal's transaction, so the member leaves and their open tasks free up together.</summary>
public sealed class MemberRemovedTaskHandler(ITaskRepository tasks) : ITransactionalEventHandler<HouseholdMemberRemoved>
{
    public Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct) =>
        tasks.UnassignMemberAsync(evt.HouseholdId, evt.UserId, ct);
}
