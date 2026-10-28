using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class ExpensePushNotificationHandlerTests
{
    private readonly Guid _householdId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _otherId = Guid.NewGuid();
    private readonly IPushSender _pushSender = Substitute.For<IPushSender>();
    private readonly IHouseholdQueries _householdQueries = Substitute.For<IHouseholdQueries>();
    private readonly IUserQuery _userQuery = Substitute.For<IUserQuery>();
    private readonly EventDispatchScope _dispatchScope = new();
    private readonly ExpensePushNotificationHandler _sut;

    public ExpensePushNotificationHandlerTests()
    {
        _sut = new ExpensePushNotificationHandler(_pushSender, _householdQueries, _userQuery, _dispatchScope);

        _householdQueries.GetMembersAsync(_householdId, Arg.Any<CancellationToken>()).Returns(
        [
            new HouseholdMemberDto(_actorId, "Alice", "Member", null),
            new HouseholdMemberDto(_otherId, "Bob", "Member", null)
        ]);
        _userQuery.GetByIdAsync(_actorId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(_actorId, "sub-a", "a@x.com", "Alice", null));
        _userQuery.GetByIdAsync(_otherId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(_otherId, "sub-b", "b@x.com", "Bob", null));
        _userQuery.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<UserProfile>)
            [
                new UserProfile(_actorId, "sub-a", "a@x.com", "Alice", null),
                new UserProfile(_otherId, "sub-b", "b@x.com", "Bob", null)
            ]);
    }

    [Test]
    public async Task ExpenseRecorded_notifies_other_members_but_not_the_actor()
    {
        var evt = new ExpenseRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId, Guid.NewGuid(),
            "Groceries", DateTimeOffset.UtcNow,
            [new FundingSource(_actorId, 5000)], [new Allocation(_actorId, 5000)], _actorId);

        await _sut.HandleAsync(evt);

        await _pushSender.Received(1).SendAsync(
            _otherId, "Alice added an expense", "Groceries — R$ 50.00",
            $"/h/{_householdId}/Expenses/Activity", Arg.Any<CancellationToken>());
        await _pushSender.DidNotReceive().SendAsync(
            _actorId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExpenseRecorded_with_CorrectedFromExpenseId_uses_Edited_wording()
    {
        var evt = new ExpenseRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId, Guid.NewGuid(),
            "Groceries (fixed)", DateTimeOffset.UtcNow,
            [new FundingSource(_actorId, 6000)], [new Allocation(_actorId, 6000)], _actorId,
            CorrectedFromExpenseId: Guid.NewGuid());

        await _sut.HandleAsync(evt);

        await _pushSender.Received(1).SendAsync(
            _otherId, "Alice edited an expense", "Groceries (fixed) — R$ 60.00",
            $"/h/{_householdId}/Expenses/Activity", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExpenseVoided_that_is_a_real_delete_notifies_other_members()
    {
        var evt = new ExpenseVoided(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId, "no longer needed",
            [new FundingSource(_actorId, 5000)], [new Allocation(_actorId, 5000)], _actorId,
            Description: "Groceries");

        await _sut.HandleAsync(evt);

        await _pushSender.Received(1).SendAsync(
            _otherId, "Alice deleted an expense", "Groceries",
            $"/h/{_householdId}/Expenses/Activity", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExpenseVoided_that_is_the_void_half_of_an_edit_is_suppressed()
    {
        var evt = new ExpenseVoided(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId, null,
            [new FundingSource(_actorId, 5000)], [new Allocation(_actorId, 5000)], _actorId,
            Description: "Groceries", CorrectedByExpenseId: Guid.NewGuid());

        await _sut.HandleAsync(evt);

        await _pushSender.DidNotReceive().SendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExpenseRecorded_swallows_exceptions_from_dependencies_instead_of_throwing()
    {
        _householdQueries.GetMembersAsync(_householdId, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<HouseholdMemberDto>>>(_ => throw new InvalidOperationException("transient DB error"));

        var evt = new ExpenseRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId, Guid.NewGuid(),
            "Groceries", DateTimeOffset.UtcNow,
            [new FundingSource(_actorId, 5000)], [new Allocation(_actorId, 5000)], _actorId);

        await _sut.HandleAsync(evt);

        await _pushSender.DidNotReceive().SendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SettlementRecorded_notifies_other_members_with_payer_and_recipient_names()
    {
        var evt = new SettlementRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId,
            PayerId: _otherId, RecipientId: _actorId, Cents: 2000, Date: DateTimeOffset.UtcNow,
            PerformedByUserId: _actorId);

        await _sut.HandleAsync(evt);

        await _pushSender.Received(1).SendAsync(
            _otherId, "Alice recorded a settlement", "Bob paid Alice R$ 20.00",
            $"/h/{_householdId}/Expenses/Activity", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Suppressed_scope_sends_no_notifications()
    {
        var recorded = new ExpenseRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId, Guid.NewGuid(),
            "Groceries", DateTimeOffset.UtcNow,
            [new FundingSource(_actorId, 1000)], [new Allocation(_otherId, 1000)], _actorId);
        var voided = new ExpenseVoided(
            Guid.NewGuid(), DateTimeOffset.UtcNow, recorded.ExpenseId, _householdId, null,
            recorded.FundingSources, recorded.Allocations, _actorId, "Groceries");
        var settled = new SettlementRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), _householdId,
            _actorId, _otherId, 500, DateTimeOffset.UtcNow, _actorId);

        using (_dispatchScope.Suppress())
        {
            await _sut.HandleAsync(recorded);
            await _sut.HandleAsync(voided);
            await _sut.HandleAsync(settled);
        }

        await Assert.That(_pushSender.ReceivedCalls().Count()).IsEqualTo(0);

        await _sut.HandleAsync(recorded);

        await Assert.That(_pushSender.ReceivedCalls().Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task Nested_suppress_keeps_outer_scope_suppressed()
    {
        using (_dispatchScope.Suppress())
        {
            using (_dispatchScope.Suppress())
            {
            }

            await Assert.That(_dispatchScope.SuppressNotifications).IsTrue();
        }

        await Assert.That(_dispatchScope.SuppressNotifications).IsFalse();
    }

    [Test]
    public async Task Household_settle_notifies_once_from_the_lead_leg_with_the_cash_total()
    {
        var id = Guid.NewGuid();
        var lead = new SettlementRecorded(Guid.NewGuid(), DateTimeOffset.UtcNow, id, _householdId, _actorId, _otherId,
            10000, DateTimeOffset.UtcNow, _actorId, Guid.NewGuid(), HouseholdSettlementId: id, HouseholdSettlementCents: 7000);
        var otherCash = lead with { SettlementId = Guid.NewGuid(), HouseholdSettlementCents = null };
        var offset = lead with { SettlementId = Guid.NewGuid(), PayerId = _otherId, RecipientId = _actorId,
            Cents = 3000, IsOffset = true, HouseholdSettlementCents = null };

        await _sut.HandleAsync(lead);
        await _sut.HandleAsync(otherCash);
        await _sut.HandleAsync(offset);

        await _pushSender.Received(1).SendAsync(
            _otherId, Arg.Any<string>(), "Alice paid Bob R$ 70.00", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _pushSender.Received(1).SendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
