using System.Globalization;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

public sealed class ExpensePushNotificationHandler(
    IPushSender pushSender,
    IHouseholdQueries householdQueries,
    IUserQuery userQuery)
    : IEventHandler<ExpenseRecorded>, IEventHandler<ExpenseVoided>, IEventHandler<SettlementRecorded>
{
    public Task HandleAsync(ExpenseRecorded evt, CancellationToken ct = default) => SafeAsync(async () =>
    {
        var actorName = await ActorNameAsync(evt.PerformedByUserId, ct);
        var isEdit = evt.CorrectedFromExpenseId is not null;
        var title = isEdit ? $"{actorName} edited an expense" : $"{actorName} added an expense";
        var body = $"{evt.Description} — R$ {FormatCents(evt.Allocations.Sum(a => a.Cents))}";
        await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, title, body, ct);
    }, ct);

    public Task HandleAsync(ExpenseVoided evt, CancellationToken ct = default)
    {
        // A void that carries CorrectedByExpenseId is the "delete" half of an edit
        // (void + resubmit) — the paired ExpenseRecorded already notified. Mirrors the
        // same suppression ActivityEntryProjection.MapVoided applies to the activity feed.
        if (evt.CorrectedByExpenseId is not null) return Task.CompletedTask;

        return SafeAsync(async () =>
        {
            var actorName = await ActorNameAsync(evt.PerformedByUserId, ct);
            var title = $"{actorName} deleted an expense";
            await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, title, evt.Description, ct);
        }, ct);
    }

    public Task HandleAsync(SettlementRecorded evt, CancellationToken ct = default) => SafeAsync(async () =>
    {
        var ids = new[] { evt.PerformedByUserId, evt.PayerId, evt.RecipientId };
        var profiles = (await userQuery.GetByIdsAsync(ids, ct)).ToDictionary(p => p.Id);
        var actorName = profiles.GetValueOrDefault(evt.PerformedByUserId)?.DisplayName ?? "Someone";
        var payerName = profiles.GetValueOrDefault(evt.PayerId)?.DisplayName ?? "Someone";
        var recipientName = profiles.GetValueOrDefault(evt.RecipientId)?.DisplayName ?? "Someone";
        var title = $"{actorName} recorded a settlement";
        var body = $"{payerName} paid {recipientName} R$ {FormatCents(evt.Cents)}";
        await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, title, body, ct);
    }, ct);

    private async Task NotifyOthersAsync(Guid householdId, Guid actorId, string title, string body, CancellationToken ct)
    {
        var members = await householdQueries.GetMembersAsync(householdId, ct);
        var url = $"/h/{householdId}/Expenses/Activity";
        var sends = members.Where(m => m.UserId != actorId)
            .Select(member => SafeAsync(() => pushSender.SendAsync(member.UserId, title, body, url, ct), ct));
        await Task.WhenAll(sends);
    }

    private async Task<string> ActorNameAsync(Guid userId, CancellationToken ct) =>
        (await userQuery.GetByIdAsync(userId, ct))?.DisplayName ?? "Someone";

    private static string FormatCents(long cents) => (cents / 100m).ToString("N2", CultureInfo.InvariantCulture);

    // Push notifications (and each individual member's delivery within them) are best-effort:
    // a failure here must never fail the already-committed write it's reacting to, block other
    // deferred handlers queued in the same event-bus flush, or stop notifying the rest of the
    // household because one member's send failed.
    private static async Task SafeAsync(Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
        }
    }
}
