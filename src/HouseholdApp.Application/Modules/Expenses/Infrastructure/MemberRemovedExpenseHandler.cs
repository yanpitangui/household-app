using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

/// <summary>Runs after the removal commits; recurring templates live in Marten, outside the Households transaction.</summary>
public sealed class MemberRemovedExpenseHandler(IExpenseMemberRemoval removal, ILogger<MemberRemovedExpenseHandler> logger)
    : IEventHandler<HouseholdMemberRemoved>
{
    public async Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct)
    {
        try
        {
            await removal.ApplyAsync(evt.HouseholdId, evt.UserId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The removal has already committed; logged so the member's templates can be fixed by hand.
            logger.LogError(ex, "Applying removal of member {UserId} to recurring expenses failed for household {HouseholdId}",
                evt.UserId, evt.HouseholdId);
        }
    }
}
