using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Time;
using Marten;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public sealed class ExpenseMemberRemovalService(
    IDocumentSession session, IExpenseQueries queries, ICurrentUser currentUser, TimeProvider time,
    IHouseholdClock clock) : IExpenseMemberRemoval
{
    public async Task<ExpenseRemovalImpact> PreviewAsync(Guid householdId, Guid userId, CancellationToken ct = default)
    {
        // Viewer-relative: positive means the other member owes the viewer (here, the member).
        var balances = await queries.GetBalancesAsync(householdId, userId, null, ct);
        var recurring = (await TemplatesWithAsync(householdId, userId, ct))
            .Select(t => (t, Outcome: MemberRemoval.For(t.FundingSources, t.Allocations, userId)))
            .Where(x => x.Outcome is not MemberRemoval.Unaffected)
            .Select(x => new RecurringRemovalImpact(x.t.Id, x.t.Description,
                x.Outcome is MemberRemoval.Ended ? RecurringRemovalEffect.Ended : RecurringRemovalEffect.Redistributed))
            .ToList();
        var plans = (await session.Query<InstallmentPlanDocument>()
                .Where(p => p.HouseholdId == householdId && p.Status == InstallmentPlanStatus.Active)
                .ToListAsync(ct))
            .Where(p => p.PayerId == userId || p.Segments.Any(s => s.Allocations.Any(a => a.UserId == userId)))
            .Select(p => p.Description)
            .ToList();
        return new ExpenseRemovalImpact(balances.Sum(b => b.Cents), recurring, plans);
    }

    public async Task ApplyAsync(Guid householdId, Guid userId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var zone = await clock.ZoneAsync(householdId, ct);
        foreach (var doc in await TemplatesWithAsync(householdId, userId, ct))
        {
            var stream = await session.Events.FetchForExclusiveWriting<RecurringExpense>(doc.Id, ct);
            if (stream.Aggregate is not { } template) continue;
            if (template.RemoveMember(userId, now, zone, currentUser.Id))
                stream.AppendMany(template.DomainEvents.ToArray());
        }
        await session.SaveChangesAsync(ct);
    }

    // Active and paused templates; the member filter runs in memory over the household's few templates.
    private async Task<IReadOnlyList<RecurringExpenseDocument>> TemplatesWithAsync(
        Guid householdId, Guid userId, CancellationToken ct) =>
        (await session.Query<RecurringExpenseDocument>()
            .Where(t => t.HouseholdId == householdId && t.Status != RecurringExpenseStatus.Ended)
            .ToListAsync(ct))
        .Where(t => t.FundingSources.Any(f => f.UserId == userId) || t.Allocations.Any(a => a.UserId == userId))
        .ToList();
}
