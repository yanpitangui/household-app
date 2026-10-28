using HouseholdApp.Application.Modules.Expenses.Application.Operations;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class TimelineCursorTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("!!!not-base64!!!")]
    [Arguments("e=garbage")]
    [Arguments("ZT1nYXJiYWdl")]
    [Arguments("ZT0yMDI2LTEwLTAxVDEwOjAwOjAwLjAwMDAwMDArMDA6MDB8bm90LWEtZ3VpZA")]
    [Arguments("ZT0yMDI2LTEwLTAxVDEwOjAwOjAwLjAwMDAwMDArMDA6MDA")]
    public async Task Decode_returns_the_default_cursor_for_a_malformed_value(string cursor)
    {
        var decoded = TimelineCursor.Decode(cursor);

        await Assert.That(decoded).IsEqualTo(default(TimelineCursor));
    }

    [Test]
    public async Task Decode_round_trips_both_positions_and_the_recent_flag()
    {
        var expense = new KeysetPoint(At, Guid.NewGuid());
        var settlement = new KeysetPoint(At.AddDays(-1), Guid.NewGuid());

        var decoded = TimelineCursor.Decode(TimelineCursor.Encode(expense, settlement, inRecent: true));

        await Assert.That(decoded.Expense).IsEqualTo(expense);
        await Assert.That(decoded.Settlement).IsEqualTo(settlement);
        await Assert.That(decoded.InRecent).IsTrue();
    }

    [Test]
    public async Task StartOfOlder_decodes_to_the_default_cursor()
    {
        await Assert.That(TimelineCursor.Decode(TimelineCursor.StartOfOlder)).IsEqualTo(default(TimelineCursor));
    }
}
