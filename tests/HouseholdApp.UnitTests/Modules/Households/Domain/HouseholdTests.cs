using HouseholdApp.Application.Modules.Households.Domain;

namespace HouseholdApp.UnitTests.Modules.Households.Domain;

public sealed class HouseholdTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Household CreateHousehold() =>
        Household.Create("Test House", OwnerId, HouseholdTimeZone.Default, Now);

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

    private static readonly Guid Ana = Guid.NewGuid(), Bia = Guid.NewGuid(), Leo = Guid.NewGuid();

    private static Household WithMembers() =>
        Household.Reconstitute(Guid.NewGuid(), "Casa", "America/Sao_Paulo", Now,
            [new(Guid.Empty, Ana, HouseholdRole.Owner), new(Guid.Empty, Bia, HouseholdRole.Admin), new(Guid.Empty, Leo, HouseholdRole.Member)]);

    [Test]
    public async Task Rename_by_admin_changes_name_and_raises_event()
    {
        var h = WithMembers();
        h.Rename(Bia, "  Casa Nova ", Now);
        await Assert.That(h.Name).IsEqualTo("Casa Nova");
        await Assert.That(h.DomainEvents.OfType<HouseholdRenamed>().Single().Name).IsEqualTo("Casa Nova");
    }

    [Test]
    public async Task Rename_by_plain_member_is_rejected()
    {
        var h = WithMembers();
        await Assert.That(() => h.Rename(Leo, "X", Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Rename_to_blank_is_rejected()
    {
        var h = WithMembers();
        await Assert.That(() => h.Rename(Ana, "   ", Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Changing_time_zone_accepts_iana_ids_only()
    {
        var h = WithMembers();
        h.ChangeTimeZone(Bia, "Europe/Lisbon", Now);
        await Assert.That(h.TimeZone).IsEqualTo("Europe/Lisbon");
        await Assert.That(() => h.ChangeTimeZone(Bia, "Mars/Olympus", Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Transfer_ownership_swaps_owner_and_previous_owner_becomes_admin()
    {
        var h = WithMembers();
        h.TransferOwnership(Ana, Leo, Now);
        await Assert.That(h.Members.Single(m => m.UserId == Leo).Role).IsEqualTo(HouseholdRole.Owner);
        await Assert.That(h.Members.Single(m => m.UserId == Ana).Role).IsEqualTo(HouseholdRole.Admin);
        var changes = h.DomainEvents.OfType<HouseholdRoleChanged>().ToList();
        await Assert.That(changes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Only_the_owner_transfers_ownership()
    {
        var h = WithMembers();
        await Assert.That(() => h.TransferOwnership(Bia, Leo, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Leave_removes_the_member_and_raises_removed()
    {
        var h = WithMembers();
        h.Leave(Leo, Now);
        await Assert.That(h.Members.Any(m => m.UserId == Leo)).IsFalse();
        await Assert.That(h.DomainEvents.OfType<HouseholdMemberRemoved>().Single().UserId).IsEqualTo(Leo);
    }

    [Test]
    public async Task Owner_cannot_leave()
    {
        var h = WithMembers();
        await Assert.That(() => h.Leave(Ana, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Members_cannot_remove_themselves_through_remove()
    {
        var h = WithMembers();
        await Assert.That(() => h.RemoveMember(Bia, Bia, Now)).Throws<InvalidOperationException>();
    }
}
