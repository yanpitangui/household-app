using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class ExpenseTimelineTests
{
    private static readonly Guid Viewer = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Third = Guid.NewGuid();

    private static ExpenseListItem Expense(string description, DateTimeOffset date) =>
        new(Guid.NewGuid(), description, date, 100, [], [], null);

    private static SettlementListItem Payment(Guid payer, Guid recipient, DateTimeOffset date) =>
        new(Guid.NewGuid(), date,
            new ExpenseListParticipantDto(payer, "P", 100, null),
            new ExpenseListParticipantDto(recipient, "R", 100, null),
            100, null);

    [Test]
    public async Task Merge_orders_newest_first_across_both_kinds()
    {
        var d1 = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var d2 = d1.AddDays(1);
        var d3 = d1.AddDays(2);

        var merged = ExpenseTimeline.Merge(
            [Expense("old", d1), Expense("new", d3)],
            [Payment(Viewer, Other, d2)]);

        await Assert.That(merged[0].Date).IsEqualTo(d3);
        await Assert.That(merged[1].Date).IsEqualTo(d2);
        await Assert.That(merged[2].Date).IsEqualTo(d1);
        await Assert.That(merged[1] is PaymentEntry).IsTrue();
        await Assert.That(((ExpenseEntry)merged[0]).Expense.Description).IsEqualTo("new");
    }

    [Test]
    public async Task Merge_puts_payment_above_expenses_later_on_the_same_day()
    {
        var d = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var merged = ExpenseTimeline.Merge([Expense("e", d.AddHours(10))], [Payment(Viewer, Other, d)]);

        await Assert.That(merged[0] is PaymentEntry).IsTrue();
        await Assert.That(merged[1] is ExpenseEntry).IsTrue();
    }

    [Test]
    public async Task Merge_puts_payment_above_expenses_on_the_same_date()
    {
        var d = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var merged = ExpenseTimeline.Merge([Expense("e", d)], [Payment(Viewer, Other, d)]);

        await Assert.That(merged[0] is PaymentEntry).IsTrue();
        await Assert.That(merged[1] is ExpenseEntry).IsTrue();
    }

    [Test]
    public async Task Merge_of_empty_inputs_is_empty()
    {
        await Assert.That(ExpenseTimeline.Merge([], [])).IsEmpty();
    }

    [Test]
    public async Task PerspectiveFor_covers_payer_recipient_and_bystander()
    {
        var p = Payment(Viewer, Other, DateTimeOffset.UtcNow);

        await Assert.That(p.PerspectiveFor(Viewer)).IsEqualTo(PaymentPerspective.ViewerPaid);
        await Assert.That(p.PerspectiveFor(Other)).IsEqualTo(PaymentPerspective.ViewerReceived);
        await Assert.That(p.PerspectiveFor(Third)).IsEqualTo(PaymentPerspective.Others);
    }
}
