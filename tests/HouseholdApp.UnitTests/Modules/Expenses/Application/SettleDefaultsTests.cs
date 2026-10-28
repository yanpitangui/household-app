using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class SettleDefaultsTests
{
    private static readonly Guid Viewer = Guid.NewGuid();
    private static readonly Guid Samla = Guid.NewGuid();
    private static readonly Guid Third = Guid.NewGuid();
    private static readonly IReadOnlyList<Guid> Members = [Viewer, Samla, Third];

    private static MemberBalance Balance(Guid userId, long cents) => new(userId, "x", cents, null);

    [Test]
    public async Task Member_owes_viewer_member_pays_viewer_full_amount()
    {
        var d = SettleDefaults.From([Balance(Samla, 31240)], Viewer, null, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Samla, Viewer, 31240));
    }

    [Test]
    public async Task Viewer_owes_member_viewer_pays_member_full_amount()
    {
        var d = SettleDefaults.From([Balance(Samla, -31240)], Viewer, null, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Viewer, Samla, 31240));
    }

    [Test]
    public async Task No_with_picks_the_first_largest_balance()
    {
        var d = SettleDefaults.From([Balance(Third, -900), Balance(Samla, 100)], Viewer, null, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Viewer, Third, 900));
    }

    [Test]
    public async Task With_selects_that_members_balance()
    {
        var d = SettleDefaults.From([Balance(Third, -900), Balance(Samla, 100)], Viewer, Samla, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Samla, Viewer, 100));
    }

    [Test]
    public async Task With_member_without_balance_defaults_viewer_to_member_with_no_amount()
    {
        var d = SettleDefaults.From([Balance(Samla, -900)], Viewer, Third, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Viewer, Third, null));
    }

    [Test]
    public async Task With_non_member_is_ignored()
    {
        var d = SettleDefaults.From([Balance(Samla, 100)], Viewer, Guid.NewGuid(), Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Samla, Viewer, 100));
    }

    [Test]
    public async Task With_viewer_is_ignored()
    {
        var d = SettleDefaults.From([Balance(Samla, 100)], Viewer, Viewer, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Samla, Viewer, 100));
    }

    [Test]
    public async Task Balance_with_non_member_is_skipped()
    {
        var d = SettleDefaults.From([Balance(Guid.NewGuid(), -5000), Balance(Samla, 100)], Viewer, null, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Samla, Viewer, 100));
    }

    [Test]
    public async Task No_balances_defaults_recipient_to_first_other_member()
    {
        var d = SettleDefaults.From([], Viewer, null, Members);

        await Assert.That(d).IsEqualTo(new SettleDefaults(Viewer, Samla, null));
    }

    [Test]
    public async Task Viewer_listed_first_is_never_the_default_recipient()
    {
        var d = SettleDefaults.From([], Viewer, null, [Viewer, Samla]);

        await Assert.That(d.RecipientId).IsEqualTo(Samla);
    }
}
