using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Households.Application.Operations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Households.Application;

public sealed class HouseholdCommandServiceTests
{
    private readonly IHouseholdRepository _repo = Substitute.For<IHouseholdRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly HouseholdCommandService _sut;

    public HouseholdCommandServiceTests()
    {
        _currentUser.Id.Returns(Guid.NewGuid());
        _sut = new HouseholdCommandService(_repo, _uow, _eventBus, new FakeTimeProvider(), _currentUser);
    }

    [Test]
    public async Task CreateAsync_saves_household_and_returns_id()
    {
        var id = await _sut.CreateAsync("My House");

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await _repo.Received(1).SaveAsync(
            Arg.Is<Household>(h => h.Name == "My House"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InviteAsync_returns_token_and_saves_invitation()
    {
        var householdId = Guid.NewGuid();
        var ownerId = _currentUser.Id;
        var household = Household.Create("House", ownerId, DateTimeOffset.UtcNow);
        _repo.GetAsync(householdId, Arg.Any<CancellationToken>()).Returns(household);

        var token = await _sut.InviteAsync(householdId);

        await Assert.That(token).IsNotEmpty();
        await _repo.Received(1).SaveInvitationAsync(Arg.Any<HouseholdInvitation>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InviteAsync_throws_when_household_not_found()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Household?)null);

        await Assert.That(async () => await _sut.InviteAsync(Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AcceptInvitationAsync_returns_false_when_consume_fails()
    {
        _repo.ConsumeInvitationAtomicAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.AcceptInvitationAsync("bad-token");

        await Assert.That(result).IsFalse();
        await _repo.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AcceptInvitationAsync_adds_member_when_token_valid()
    {
        var ownerId = Guid.NewGuid();
        var newUserId = _currentUser.Id;
        var household = Household.Create("House", ownerId, DateTimeOffset.UtcNow);
        var invitation = household.Invite(ownerId, DateTimeOffset.UtcNow, TimeSpan.FromDays(7));
        household.ClearEvents();

        _repo.ConsumeInvitationAtomicAsync(invitation.Token, newUserId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repo.GetInvitationByTokenAsync(invitation.Token, Arg.Any<CancellationToken>())
            .Returns(invitation);
        _repo.GetAsync(household.Id, Arg.Any<CancellationToken>())
            .Returns(household);

        var result = await _sut.AcceptInvitationAsync(invitation.Token);

        await Assert.That(result).IsTrue();
        await Assert.That(household.Members.Any(m => m.UserId == newUserId)).IsTrue();
    }

    [Test]
    public async Task RemoveMemberAsync_throws_when_household_not_found()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Household?)null);

        await Assert.That(async () => await _sut.RemoveMemberAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveMemberAsync_removes_member_from_household()
    {
        var ownerId = _currentUser.Id;
        var memberId = Guid.NewGuid();
        var household = Household.Create("House", ownerId, DateTimeOffset.UtcNow);
        var invitation = household.Invite(ownerId, DateTimeOffset.UtcNow, TimeSpan.FromDays(7));
        household.AcceptInvitation(invitation, memberId, DateTimeOffset.UtcNow);
        household.ClearEvents();

        _repo.GetAsync(household.Id, Arg.Any<CancellationToken>()).Returns(household);

        await _sut.RemoveMemberAsync(household.Id, memberId);

        await Assert.That(household.Members.All(m => m.UserId != memberId)).IsTrue();
    }

    [Test]
    public async Task ChangeMemberRoleAsync_throws_on_unknown_role()
    {
        await Assert.That(async () => await _sut.ChangeMemberRoleAsync(Guid.NewGuid(), Guid.NewGuid(), "SuperAdmin"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RevokeInvitationAsync_throws_when_invitation_not_found()
    {
        _repo.GetInvitationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdInvitation?)null);

        await Assert.That(async () => await _sut.RevokeInvitationAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RevokeInvitationAsync_revokes_invitation()
    {
        var ownerId = Guid.NewGuid();
        var household = Household.Create("House", ownerId, DateTimeOffset.UtcNow);
        var invitation = household.Invite(ownerId, DateTimeOffset.UtcNow, TimeSpan.FromDays(7));

        _repo.GetInvitationAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);

        await _sut.RevokeInvitationAsync(household.Id, invitation.Id);

        await Assert.That(invitation.Status).IsEqualTo(InvitationStatus.Revoked);
    }
}
