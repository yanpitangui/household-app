using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class UpcomingBuilderTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Leo = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static InstallmentPlanDocument Plan(DateOnly first, int count = 5) => new()
    {
        Id = Guid.NewGuid(), ExpenseGroupId = Group, Description = "Car repair", PayerId = Ana,
        TotalCents = 150000, Count = count, FirstDate = first, Status = InstallmentPlanStatus.Active,
        Segments = [new SplitSegment(1, [new Allocation(Ana, 75000), new Allocation(Leo, 75000)])]
    };

    private static RecurrenceSchedule Schedule(RecurrenceFrequency frequency, int y, int m, int d) =>
        new(frequency, new DateOnly(y, m, d), new TimeOnly(9, 0));

    private static RecurringTemplate Template(RecurrenceSchedule schedule, long cents = 8000) => new(
        Guid.NewGuid(), "Cleaning", Group, schedule,
        [new FundingSource(Ana, cents)], [new Allocation(Ana, cents / 2), new Allocation(Leo, cents / 2)]);

    [Test]
    public async Task Plan_installments_in_the_window_that_are_not_created_yet()
    {
        var plan = Plan(new DateOnly(2026, 10, 10)); // #2 on Nov 10 is in the window, #3 on Dec 10 is out

        var summary = UpcomingBuilder.Build([(plan, new HashSet<int> { 1 })], [], Now, Leo);

        var item = summary.Items.Single();
        await Assert.That(item.Number).IsEqualTo(2);
        await Assert.That(item.FirstDate).IsEqualTo(new DateOnly(2026, 11, 10));
        await Assert.That(item.TotalCents).IsEqualTo(30000L);
        await Assert.That(item.ViewerShareCents).IsEqualTo(15000L);
    }

    [Test]
    public async Task A_future_dated_down_payment_is_listed_once_as_number_zero()
    {
        var plan = Plan(new DateOnly(2026, 11, 20));
        plan.DownPaymentCents = 50000;
        plan.Segments = [new SplitSegment(0, [new Allocation(Ana, 75000), new Allocation(Leo, 75000)])];
        plan.PurchaseDate = new DateOnly(2026, 11, 5); // the down payment (Nov 5) is in the window, #1 on Nov 20 too

        var summary = UpcomingBuilder.Build([(plan, new HashSet<int>())], [], Now, Leo);

        var item = summary.Items.Single();
        await Assert.That(item.Number).IsEqualTo(0);
        await Assert.That(item.Occurrences).IsEqualTo(2);
        await Assert.That(item.FirstDate).IsEqualTo(new DateOnly(2026, 11, 5));
        await Assert.That(item.TotalCents).IsEqualTo(70000L); // 50000 down + 20000 for #1
        await Assert.That(item.ViewerShareCents).IsEqualTo(35000L); // Leo: 25000 + 10000
    }

    [Test]
    public async Task A_past_due_installment_that_was_never_created_is_left_out()
    {
        var plan = Plan(new DateOnly(2026, 9, 10)); // #1 Sep 10 and #2 Oct 10 are past due, #3 Nov 10 is in the window

        var summary = UpcomingBuilder.Build([(plan, new HashSet<int>())], [], Now, Leo);

        var item = summary.Items.Single();
        await Assert.That(item.Number).IsEqualTo(3);
        await Assert.That(item.Occurrences).IsEqualTo(1);
    }

    [Test]
    public async Task A_split_changed_mid_plan_uses_the_matching_segment_for_the_viewer_share()
    {
        var plan = Plan(new DateOnly(2026, 10, 10));
        plan.Segments =
        [
            new SplitSegment(1, [new Allocation(Ana, 75000), new Allocation(Leo, 75000)]),
            new SplitSegment(2, [new Allocation(Ana, 100000), new Allocation(Leo, 50000)])
        ];

        var summary = UpcomingBuilder.Build([(plan, new HashSet<int> { 1 })], [], Now, Leo);

        await Assert.That(summary.Items.Single().ViewerShareCents).IsEqualTo(10000L); // #2: Leo pays a third
    }

    [Test]
    public async Task Weekly_templates_fold_into_one_row_with_a_count()
    {
        var weekly = Template(Schedule(RecurrenceFrequency.Weekly, 2026, 10, 31)); // Saturdays 09:00

        var summary = UpcomingBuilder.Build([], [weekly], Now, Leo);

        var item = summary.Items.Single();
        await Assert.That(item.Occurrences).IsEqualTo(4);   // Oct 31, Nov 7, 14, 21
        await Assert.That(item.FirstDate).IsEqualTo(new DateOnly(2026, 10, 31));
        await Assert.That(item.TotalCents).IsEqualTo(32000L);
        await Assert.That(item.ViewerShareCents).IsEqualTo(16000L);
    }

    [Test]
    public async Task Annual_templates_appear_only_in_their_month()
    {
        var inWindow = Template(Schedule(RecurrenceFrequency.Annually, 2026, 11, 15));
        var outOfWindow = Template(Schedule(RecurrenceFrequency.Annually, 2026, 3, 15));

        var summary = UpcomingBuilder.Build([], [inWindow, outOfWindow], Now, Leo);

        await Assert.That(summary.Items.Select(i => i.SourceId)).IsEquivalentTo(new[] { inWindow.Id });
    }

    [Test]
    public async Task An_occurrence_earlier_today_already_fired_and_is_left_out()
    {
        var thursdays = Template(Schedule(RecurrenceFrequency.Weekly, 2026, 10, 29)); // today (Thu) 09:00 < now 12:00

        var summary = UpcomingBuilder.Build([], [thursdays], Now, Leo);

        var item = summary.Items.Single();
        await Assert.That(item.FirstDate).IsEqualTo(new DateOnly(2026, 11, 5));
        await Assert.That(item.Occurrences).IsEqualTo(4); // Nov 5, 12, 19, 26; the window ends before Nov 28
    }

    [Test]
    public async Task Totals_sum_every_row_and_rows_sort_by_date()
    {
        var plan = Plan(new DateOnly(2026, 10, 10));
        var weekly = Template(Schedule(RecurrenceFrequency.Weekly, 2026, 10, 31));

        var summary = UpcomingBuilder.Build([(plan, new HashSet<int> { 1 })], [weekly], Now, Leo);

        await Assert.That(summary.TotalCents).IsEqualTo(62000L);
        await Assert.That(summary.ViewerShareCents).IsEqualTo(31000L);
        await Assert.That(summary.Items[0].Source).IsEqualTo(UpcomingSource.Recurring);
    }

    [Test]
    public async Task A_template_on_the_31st_lands_on_the_last_day_of_a_shorter_month()
    {
        var monthEnd = Template(Schedule(RecurrenceFrequency.Monthly, 2026, 1, 31));
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        var summary = UpcomingBuilder.Build([], [monthEnd], now, Leo);

        await Assert.That(summary.Items.Single().FirstDate).IsEqualTo(new DateOnly(2026, 11, 30));
    }
}
