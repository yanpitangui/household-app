using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Jobs;

public sealed class RecurringExpenseJobs(IExpenseCommands expenseCommands) : ITickerFunction<Guid>
{
    public const string FunctionName = nameof(RecurringExpenseJobs);

    public Task ExecuteAsync(TickerFunctionContext<Guid> context, CancellationToken ct) =>
        expenseCommands.SpawnRecurringExpenseAsync(context.Request, ct);
}
