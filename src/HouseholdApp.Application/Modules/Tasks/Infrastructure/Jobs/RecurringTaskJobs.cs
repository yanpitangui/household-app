using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;

namespace HouseholdApp.Application.Modules.Tasks.Infrastructure.Jobs;

public sealed class RecurringTaskJobs(ITaskCommands taskCommands) : ITickerFunction<Guid>
{
    public const string FunctionName = nameof(RecurringTaskJobs);

    public Task ExecuteAsync(TickerFunctionContext<Guid> context, CancellationToken ct) =>
        taskCommands.SpawnRecurringTaskAsync(context.Request, ct);
}
