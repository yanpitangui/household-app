using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Time;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

/// <summary>
/// Upcoming: everything due in the next 30 days from active plans and active recurring templates. Read-only and
/// never in a balance. Templates are read with the same schedule the sweep fires, so the list matches what will
/// actually be created, including a template on the 31st landing on the last day of a 30-day month.
/// </summary>
public static class UpcomingBuilder
{
    public const int WindowDays = 30;

    public static UpcomingSummary Build(
        IReadOnlyList<(InstallmentPlanDocument Plan, HashSet<int> CreatedNumbers)> plans,
        IReadOnlyList<RecurringTemplate> templates,
        DateTimeOffset now, DateOnly today, TimeZoneInfo zone, Guid viewerId)
    {
        var end = today.AddDays(WindowDays);
        var items = new List<UpcomingItem>();
        // The window closes at the start of the day after it, on the household's wall clock.
        var endInstant = new RecurrenceSchedule(RecurrenceFrequency.Daily, end, TimeOnly.MinValue).In(zone).At(0);

        foreach (var (plan, created) in plans)
        {
            var terms = plan.ToTerms();
            var numbers = terms.Numbers
                .Where(n => !created.Contains(n))
                .Select(n => (Number: n, Due: terms.DueDate(n)))
                .Where(x => x.Due >= today && x.Due < end)
                .ToList();
            if (numbers.Count == 0) continue;

            var total = numbers.Sum(x => terms.Amount(x.Number));
            var share = numbers.Sum(x => terms.Allocations(plan.Segments, x.Number)
                .FirstOrDefault(a => a.UserId == viewerId)?.Cents ?? 0);
            items.Add(new UpcomingItem(
                UpcomingSource.Installment, plan.Id, plan.Description, plan.ExpenseGroupId, numbers[0].Due,
                numbers.Count, numbers[0].Number, plan.Count, null, plan.PayerId, total, share));
        }

        foreach (var template in templates)
        {
            var zoned = template.Schedule.In(zone);
            var dates = zoned.Between(now, endInstant)
                .Select(zoned.DateOf)
                .ToList();
            if (dates.Count == 0) continue;

            var amount = template.FundingSources.Sum(f => f.Cents);
            var share = template.Allocations.FirstOrDefault(a => a.UserId == viewerId)?.Cents ?? 0;
            items.Add(new UpcomingItem(
                UpcomingSource.Recurring, template.Id, template.Description, template.GroupId, dates[0], dates.Count,
                null, null, template.Schedule.Frequency, template.FundingSources.FirstOrDefault()?.UserId ?? Guid.Empty,
                amount * dates.Count, share * dates.Count));
        }

        var sorted = items.OrderBy(i => i.FirstDate).ThenBy(i => i.Description).ToList();
        return new UpcomingSummary(sorted, sorted.Sum(i => i.TotalCents), sorted.Sum(i => i.ViewerShareCents));
    }
}
