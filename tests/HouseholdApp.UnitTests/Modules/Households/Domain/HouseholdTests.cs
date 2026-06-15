using HouseholdApp.Application.Modules.Households.Domain;

namespace HouseholdApp.UnitTests.Modules.Households.Domain;

public sealed class HouseholdTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Household CreateHousehold() =>
        Household.Create("Test House", OwnerId, Now);

    [Test]
    public async Task Create_sets_name_and_owner_member()
    {
        var household = CreateHousehold();

        await Assert.That(household.Name).IsEqualTo("Test House");
        await Assert.That(household.Members.Count).IsEqualTo(1);
        await Assert.That(household.Members[0].UserId).IsEqualTo(OwnerId);
        await Assert.That(household.Members[0].Role).IsEqualTo(HouseholdRole.Owner);
    }

    [Test]
    public async Task Create_raises_HouseholdCreated_event()
    {
        var household = CreateHousehold();

        await Assert.That(household.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(household.DomainEvents[0] is HouseholdCreated).IsTrue();
    }

    [Test]
    public async Task Invite_by_non_member_throws()
    {
        var household = CreateHousehold();

        await Assert.That(() =>
            household.Invite(Guid.NewGuid(), Now, TimeSpan.FromDays(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Invite_by_plain_member_throws()
    {
        var household = CreateHousehold();
        var memberId = Guid.NewGuid();
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        household.AcceptInvitation(invitation, memberId, Now);
        household.ClearEvents();

        await Assert.That(() =>
            household.Invite(memberId, Now, TimeSpan.FromDays(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Invite_by_owner_succeeds_and_raises_events()
    {
        var household = CreateHousehold();
        household.ClearEvents();

        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));

        await Assert.That(invitation).IsNotNull();
        await Assert.That(household.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(household.DomainEvents[0] is HouseholdMemberInvited).IsTrue();
    }

    [Test]
    public async Task AcceptInvitation_adds_member_and_raises_event()
    {
        var household = CreateHousehold();
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        var newUserId = Guid.NewGuid();
        household.ClearEvents();

        household.AcceptInvitation(invitation, newUserId, Now);

        await Assert.That(household.Members.Count).IsEqualTo(2);
        await Assert.That(household.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(household.DomainEvents[0] is HouseholdMemberJoined).IsTrue();
    }

    [Test]
    public async Task RemoveMember_by_non_admin_throws()
    {
        var household = CreateHousehold();
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        var memberId = Guid.NewGuid();
        household.AcceptInvitation(invitation, memberId, Now);
        household.ClearEvents();

        var invitation2 = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        var memberId2 = Guid.NewGuid();
        household.AcceptInvitation(invitation2, memberId2, Now);
        household.ClearEvents();

        await Assert.That(() =>
            household.RemoveMember(memberId, memberId2, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveMember_owner_throws()
    {
        var household = CreateHousehold();

        await Assert.That(() =>
            household.RemoveMember(OwnerId, OwnerId, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveMember_by_owner_removes_and_raises_event()
    {
        var household = CreateHousehold();
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        var memberId = Guid.NewGuid();
        household.AcceptInvitation(invitation, memberId, Now);
        household.ClearEvents();

        household.RemoveMember(OwnerId, memberId, Now);

        await Assert.That(household.Members.Count).IsEqualTo(1);
        await Assert.That(household.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(household.DomainEvents[0] is HouseholdMemberRemoved).IsTrue();
    }

    [Test]
    public async Task ChangeRole_by_non_owner_throws()
    {
        var household = CreateHousehold();
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        var adminId = Guid.NewGuid();
        household.AcceptInvitation(invitation, adminId, Now);
        household.ChangeRole(OwnerId, adminId, HouseholdRole.Admin, Now);
        household.ClearEvents();

        var invitation2 = household.Invite(adminId, Now, TimeSpan.FromDays(1));
        var memberId = Guid.NewGuid();
        household.AcceptInvitation(invitation2, memberId, Now);
        household.ClearEvents();

        await Assert.That(() =>
            household.ChangeRole(adminId, memberId, HouseholdRole.Admin, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ChangeRole_on_owner_throws()
    {
        var household = CreateHousehold();

        await Assert.That(() =>
            household.ChangeRole(OwnerId, OwnerId, HouseholdRole.Member, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ChangeRole_by_owner_raises_event()
    {
        var household = CreateHousehold();
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        var memberId = Guid.NewGuid();
        household.AcceptInvitation(invitation, memberId, Now);
        household.ClearEvents();

        household.ChangeRole(OwnerId, memberId, HouseholdRole.Admin, Now);

        await Assert.That(household.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(household.DomainEvents[0] is HouseholdRoleChanged).IsTrue();
    }
}
