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
    IUserQuery userQuery,
    EventDispatchScope dispatchScope)
    : IEventHandler<ExpenseRecorded>, IEventHandler<ExpenseVoided>, IEventHandler<SettlementRecorded>,
      IEventHandler<InstallmentPlanCreated>, IEventHandler<InstallmentPlanPaidEarly>, IEventHandler<InstallmentPlanCancelled>
{
    public Task HandleAsync(ExpenseRecorded evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications || evt.CoveredByPlanEvent ? Task.CompletedTask : SafeAsync(async () =>
    {
        var actorName = await ActorNameAsync(evt.PerformedByUserId, ct);
        if (evt.Installment is { } link && evt.CorrectedFromExpenseId is null)
        {
            var paid = FormatCents(evt.FundingSources.Sum(f => f.Cents));
            var isDown = link.Number == 0;
            var installmentTitle = isDown
                ? $"{actorName} added a down payment"
                : $"{actorName} added installment {link.Number}/{link.Count}";
            var amount = isDown
                ? $"{evt.Description} — down payment R$ {paid}"
                : $"{evt.Description} {link.Number}/{link.Count} — R$ {paid}";
            await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, installmentTitle,
                member => amount + ShareSuffix(evt.Allocations, member, ""), PlanUrl(evt.HouseholdId, link.PlanId), ct);
            return;
        }

        var isEdit = evt.CorrectedFromExpenseId is not null;
        var title = isEdit ? $"{actorName} edited an expense" : $"{actorName} added an expense";
        var body = $"{evt.Description} — R$ {FormatCents(evt.Allocations.Sum(a => a.Cents))}";
        await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, title, body, ct);
    }, ct);

    public Task HandleAsync(ExpenseVoided evt, CancellationToken ct = default)
    {
        // A void that carries CorrectedByExpenseId is the "delete" half of an edit
        // (void + resubmit) — the paired ExpenseRecorded already notified. Mirrors the
        // same suppression ActivityEntryProjection.MapVoided applies to the activity feed. The voids of a plan
        // cancel are described by the cancel event.
        if (dispatchScope.SuppressNotifications || evt.CorrectedByExpenseId is not null || evt.CoveredByPlanEvent)
            return Task.CompletedTask;

        return SafeAsync(async () =>
        {
            var actorName = await ActorNameAsync(evt.PerformedByUserId, ct);
            var title = $"{actorName} deleted an expense";
            await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, title, evt.Description, ct);
        }, ct);
    }

    public Task HandleAsync(SettlementRecorded evt, CancellationToken ct = default)
    {
        // A household settle notifies once, from its lead leg, with the cash actually paid.
        if (dispatchScope.SuppressNotifications || evt.IsOffset) return Task.CompletedTask;
        if (evt.HouseholdSettlementId is { } householdSettlementId && evt.SettlementId != householdSettlementId)
            return Task.CompletedTask;
        var cents = evt.HouseholdSettlementCents ?? evt.Cents;

        return SafeAsync(async () =>
        {
            var ids = new[] { evt.PerformedByUserId, evt.PayerId, evt.RecipientId };
            var profiles = (await userQuery.GetByIdsAsync(ids, ct)).ToDictionary(p => p.Id);
            var actorName = profiles.GetValueOrDefault(evt.PerformedByUserId)?.DisplayName ?? "Someone";
            var payerName = profiles.GetValueOrDefault(evt.PayerId)?.DisplayName ?? "Someone";
            var recipientName = profiles.GetValueOrDefault(evt.RecipientId)?.DisplayName ?? "Someone";
            var title = $"{actorName} recorded a settlement";
            var body = $"{payerName} paid {recipientName} R$ {FormatCents(cents)}";
            await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, title, body, ct);
        }, ct);
    }

    public Task HandleAsync(InstallmentPlanCreated evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications ? Task.CompletedTask : SafeAsync(async () =>
    {
        var actorName = await ActorNameAsync(evt.PerformedByUserId, ct);
        var terms = new InstallmentTerms(
            evt.TotalCents, evt.Count, evt.DownPaymentCents, evt.PurchaseDate, evt.FirstDate);
        var first = terms.Allocations([new SplitSegment(terms.FirstNumber, evt.Allocations)], 1);
        var installments = $"{evt.Count}× R$ {FormatCents(terms.Amount(1))}";
        var body = terms.HasDownPayment
            ? $"{evt.Description} — R$ {FormatCents(terms.DownPaymentCents)} down + {installments}"
            : $"{evt.Description} — {installments}";
        await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, $"{actorName} created an installment plan",
            member => body + ShareSuffix(first, member, "/month"), PlanUrl(evt.HouseholdId, evt.PlanId), ct);
    }, ct);

    public Task HandleAsync(InstallmentPlanPaidEarly evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications ? Task.CompletedTask : SafeAsync(async () =>
    {
        var actorName = await ActorNameAsync(evt.PerformedByUserId, ct);
        var body = $"{evt.Description} {evt.FromNumber}–{evt.Count}/{evt.Count} — R$ {FormatCents(evt.PayoffCents)} (of R$ {FormatCents(evt.RemainingCents)})";
        await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, $"{actorName} paid an installment plan early",
            _ => body, PlanUrl(evt.HouseholdId, evt.PlanId), ct);
    }, ct);

    public Task HandleAsync(InstallmentPlanCancelled evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications ? Task.CompletedTask : SafeAsync(async () =>
    {
        var actorName = await ActorNameAsync(evt.PerformedByUserId, ct);
        var body = evt.VoidedInstallments.Count > 0
            ? $"{evt.Description} · voided {evt.VoidedInstallments.Count} installments"
            : evt.Description;
        await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, $"{actorName} cancelled an installment plan",
            _ => body, PlanUrl(evt.HouseholdId, evt.PlanId), ct);
    }, ct);

    private static string PlanUrl(Guid householdId, Guid planId) => $"/h/{householdId}/Expenses/Plans/{planId}";

    private static string ShareSuffix(IReadOnlyList<Allocation> allocations, Guid member, string unit) =>
        allocations.FirstOrDefault(a => a.UserId == member) is { Cents: > 0 } share
            ? $" · your share R$ {FormatCents(share.Cents)}{unit}"
            : "";

    private Task NotifyOthersAsync(Guid householdId, Guid actorId, string title, string body, CancellationToken ct) =>
        NotifyOthersAsync(householdId, actorId, title, _ => body, $"/h/{householdId}/Expenses/Activity", ct);

    private async Task NotifyOthersAsync(
        Guid householdId, Guid actorId, string title, Func<Guid, string> body, string url, CancellationToken ct)
    {
        var members = await householdQueries.GetMembersAsync(householdId, ct);
        var sends = members.Where(m => m.UserId != actorId)
            .Select(member => SafeAsync(() => pushSender.SendAsync(member.UserId, title, body(member.UserId), url, ct), ct));
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
