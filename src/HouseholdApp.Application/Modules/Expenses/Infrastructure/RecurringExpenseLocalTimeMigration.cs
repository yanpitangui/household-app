using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Time;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

/// <summary>
/// Moves each recurring template entered in UTC to its household's wall clock, keeping the instants: the template gets a
/// <see cref="RecurringExpenseScheduleLocalized"/> event and its sweep resumes. Runs at every start and only touches
/// templates not yet local (ended ones included, so their pages read consistently), so nodes starting together are safe.
/// The sessions carry no event listener, so nothing is published. Temporary: removed in the release after this one is
/// deployed, together with the sweep's <c>ScheduleIsLocal</c> filter (the event type and its <c>Apply</c> stay for replay).
/// </summary>
internal sealed class RecurringExpenseLocalTimeMigration(
    IDocumentStore store,
    TimeProvider time,
    IServiceScopeFactory scopes,
    ILogger<RecurringExpenseLocalTimeMigration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken ct)
    {
        IReadOnlyList<RecurringExpenseDocument> pending;
        await using (var query = store.QuerySession())
            pending = await query.Query<RecurringExpenseDocument>().Where(t => !t.ScheduleIsLocal).ToListAsync(ct);

        foreach (var doc in pending)
        {
            try
            {
                await LocalizeAsync(doc.Id, doc.HouseholdId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Localizing recurring expense {RecurringExpenseId} failed; retrying on the next start", doc.Id);
            }
        }
    }

    private async Task LocalizeAsync(Guid id, Guid householdId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var zone = await scope.ServiceProvider.GetRequiredService<IHouseholdClock>().ZoneAsync(householdId, ct);

        await using var session = store.LightweightSession();
        var stream = await session.Events.FetchForExclusiveWriting<RecurringExpense>(id, ct);
        if (stream.Aggregate is not { } template || !template.Localize(zone, time.GetUtcNow())) return;

        stream.AppendMany(template.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
    }
}
