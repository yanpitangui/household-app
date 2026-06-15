using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

public sealed class DefaultExpenseGroupHandler(IExpenseCommands commands) : IEventHandler<HouseholdCreated>
{
    public Task HandleAsync(HouseholdCreated evt, CancellationToken ct) =>
        commands.CreateExpenseGroupAsync(evt.HouseholdId, "General", null, ct);
}
