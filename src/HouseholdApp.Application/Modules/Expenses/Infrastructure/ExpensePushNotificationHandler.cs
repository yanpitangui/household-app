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
      IEventHandler<InstallmentPlanCreated>, IEventHandler<InstallmentPlanPaidEarly>, IEventHandler<InstallmentPlanCancelled>,
      IEventHandler<RecurringExpenseCreated>, IEventHandler<RecurringExpenseEnded>
{
    public Task HandleAsync(ExpenseRecorded evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications || evt.CoveredByPlanEvent ? Task.CompletedTask : SafeAsync(async () =>
    {
        if (evt.Installment is { } link && evt.CorrectedFromExpenseId is null)
        {
            var paid = evt.FundingSources.Sum(f => f.Cents);
            var isDown = link.Number == 0;
            await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, PlanUrl(evt.HouseholdId, link.PlanId), (t, r) =>
            {
                var actor = t.Name(r.Name(evt.PerformedByUserId));
                var title = isDown
                    ? t.Format("DownPaymentAdded", actor)
                    : t.Format("InstallmentAdded", actor, link.Number, link.Count);
                var body = isDown
                    ? t.Format("DownPaymentBody", evt.Description, t.Money(paid))
                    : t.Format("InstallmentBody", evt.Description, link.Number, link.Count, t.Money(paid));
                return (title, WithShare(t, body, evt.Allocations, r.UserId, "YourShare"));
            }, ct);
            return;
        }

        var titleKey = evt.CorrectedFromExpenseId is not null ? "ExpenseEdited" : "ExpenseAdded";
        var total = evt.Allocations.Sum(a => a.Cents);
        await NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, ActivityUrl(evt.HouseholdId), (t, r) =>
            (t.Format(titleKey, t.Name(r.Name(evt.PerformedByUserId))), t.Format("AmountBody", evt.Description, t.Money(total))), ct);
    }, ct);

    public Task HandleAsync(ExpenseVoided evt, CancellationToken ct = default)
    {
        // A void that carries CorrectedByExpenseId is the "delete" half of an edit
        // (void + resubmit) — the paired ExpenseRecorded already notified. Mirrors the
        // same suppression ActivityEntryProjection.MapVoided applies to the activity feed. The voids of a plan
        // cancel are described by the cancel event.
        if (dispatchScope.SuppressNotifications || evt.CorrectedByExpenseId is not null || evt.CoveredByPlanEvent)
            return Task.CompletedTask;

        return SafeAsync(() => NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, ActivityUrl(evt.HouseholdId), (t, r) =>
            (t.Format("ExpenseDeleted", t.Name(r.Name(evt.PerformedByUserId))), evt.Description), ct), ct);
    }

    public Task HandleAsync(SettlementRecorded evt, CancellationToken ct = default)
    {
        // A household settle notifies once, from its lead leg, with the cash actually paid.
        if (dispatchScope.SuppressNotifications || evt.IsOffset) return Task.CompletedTask;
        if (evt.HouseholdSettlementId is { } householdSettlementId && evt.SettlementId != householdSettlementId)
            return Task.CompletedTask;
        var cents = evt.HouseholdSettlementCents ?? evt.Cents;

        return SafeAsync(() => NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, ActivityUrl(evt.HouseholdId), (t, r) =>
            (t.Format("SettlementRecorded", t.Name(r.Name(evt.PerformedByUserId))),
             t.Format("SettlementBody", t.Name(r.Name(evt.PayerId)), t.Name(r.Name(evt.RecipientId)), t.Money(cents))),
            ct, evt.PayerId, evt.RecipientId), ct);
    }

    public Task HandleAsync(InstallmentPlanCreated evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications ? Task.CompletedTask : SafeAsync(() =>
    {
        var terms = new InstallmentTerms(
            evt.TotalCents, evt.Count, evt.DownPaymentCents, evt.PurchaseDate, evt.FirstDate);
        var first = terms.Allocations([new SplitSegment(terms.FirstNumber, evt.Allocations)], 1);
        return NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, PlanUrl(evt.HouseholdId, evt.PlanId), (t, r) =>
        {
            var installments = t.Format("PlanInstallments", evt.Count, t.Money(terms.Amount(1)));
            var body = terms.HasDownPayment
                ? t.Format("PlanBodyWithDown", evt.Description, t.Money(terms.DownPaymentCents), installments)
                : t.Format("AmountBody", evt.Description, installments);
            return (t.Format("PlanCreated", t.Name(r.Name(evt.PerformedByUserId))),
                WithShare(t, body, first, r.UserId, "YourSharePerMonth"));
        }, ct);
    }, ct);

    public Task HandleAsync(InstallmentPlanPaidEarly evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications ? Task.CompletedTask : SafeAsync(() =>
        NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, PlanUrl(evt.HouseholdId, evt.PlanId), (t, r) =>
            (t.Format("PlanPaidEarly", t.Name(r.Name(evt.PerformedByUserId))),
             t.Format("PlanPaidEarlyBody", evt.Description, evt.FromNumber, evt.Count,
                 t.Money(evt.PayoffCents), t.Money(evt.RemainingCents))), ct), ct);

    public Task HandleAsync(InstallmentPlanCancelled evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications ? Task.CompletedTask : SafeAsync(() =>
        NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, PlanUrl(evt.HouseholdId, evt.PlanId), (t, r) =>
            (t.Format("PlanCancelled", t.Name(r.Name(evt.PerformedByUserId))),
             evt.VoidedInstallments.Count > 0
                 ? t.Format("PlanCancelledVoided", evt.Description, evt.VoidedInstallments.Count)
                 : evt.Description), ct), ct);

    public Task HandleAsync(RecurringExpenseCreated evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications || evt.Imported ? Task.CompletedTask : SafeAsync(() =>
        NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, RecurringUrl(evt.HouseholdId), (t, r) =>
            (t.Format("RecurringCreated", t.Name(r.Name(evt.PerformedByUserId))),
             WithShare(t, t.Format("AmountBody", evt.Description, t.Money(evt.FundingSources.Sum(f => f.Cents))),
                 evt.Allocations, r.UserId, "YourShare")), ct), ct);

    public Task HandleAsync(RecurringExpenseEnded evt, CancellationToken ct = default) =>
        dispatchScope.SuppressNotifications || evt.Imported ? Task.CompletedTask : SafeAsync(() =>
        NotifyOthersAsync(evt.HouseholdId, evt.PerformedByUserId, RecurringUrl(evt.HouseholdId), (t, r) =>
            (t.Format("RecurringEnded", t.Name(r.Name(evt.PerformedByUserId))), evt.Description), ct), ct);

    private static string ActivityUrl(Guid householdId) => $"/h/{householdId}/Expenses/Activity";
    private static string PlanUrl(Guid householdId, Guid planId) => $"/h/{householdId}/Expenses/Plans/{planId}";
    private static string RecurringUrl(Guid householdId) => $"/h/{householdId}/Expenses/Recurring";

    private static string WithShare(
        ExpenseNotificationText t, string body, IReadOnlyList<Allocation> allocations, Guid member, string key) =>
        allocations.FirstOrDefault(a => a.UserId == member) is { Cents: > 0 } share
            ? t.Format(key, body, t.Money(share.Cents))
            : body;

    // The people a notification can name, plus the member it is being written for.
    private sealed record Recipient(Guid UserId, IReadOnlyDictionary<Guid, UserProfile> Profiles)
    {
        public string? Name(Guid userId) => Profiles.GetValueOrDefault(userId)?.DisplayName;
    }

    // Each member other than the actor gets the notification in their own language (identity.users.locale).
    private async Task NotifyOthersAsync(
        Guid householdId, Guid actorId, string url,
        Func<ExpenseNotificationText, Recipient, (string Title, string Body)> render,
        CancellationToken ct, params Guid[] mentioned)
    {
        var members = await householdQueries.GetMembersAsync(householdId, ct);
        var ids = members.Select(m => m.UserId).Append(actorId).Concat(mentioned).Distinct();
        var profiles = (await userQuery.GetByIdsAsync(ids, ct)).ToDictionary(p => p.Id);

        var sends = members.Where(m => m.UserId != actorId).Select(member => SafeAsync(() =>
        {
            var text = ExpenseNotificationText.For(profiles.GetValueOrDefault(member.UserId)?.Locale);
            var (title, body) = render(text, new Recipient(member.UserId, profiles));
            return pushSender.SendAsync(member.UserId, title, body, url, ct);
        }, ct));
        await Task.WhenAll(sends);
    }

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
