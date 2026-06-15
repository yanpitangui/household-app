using HouseholdApp.Application.Modules.Households.Domain;

namespace HouseholdApp.UnitTests.Modules.Households.Domain;

public sealed class HouseholdInvitationTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static (Household household, HouseholdInvitation invitation) CreatePendingInvitation()
    {
        var household = Household.Create("Test House", OwnerId, Now);
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromDays(1));
        return (household, invitation);
    }

    [Test]
    public async Task Consume_with_valid_invitation_succeeds()
    {
        var (household, invitation) = CreatePendingInvitation();
        var userId = Guid.NewGuid();

        household.AcceptInvitation(invitation, userId, Now);

        await Assert.That(invitation.Status).IsEqualTo(InvitationStatus.Accepted);
        await Assert.That(invitation.AcceptedBy).IsEqualTo(userId);
    }

    [Test]
    public async Task Consume_expired_invitation_throws()
    {
        var household = Household.Create("Test House", OwnerId, Now);
        var invitation = household.Invite(OwnerId, Now, TimeSpan.FromMilliseconds(1));
        var future = Now.AddDays(1);

        await Assert.That(() =>
            household.AcceptInvitation(invitation, Guid.NewGuid(), future))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Consume_already_accepted_invitation_throws()
    {
        var (household, invitation) = CreatePendingInvitation();
        household.AcceptInvitation(invitation, Guid.NewGuid(), Now);

        await Assert.That(() =>
            household.AcceptInvitation(invitation, Guid.NewGuid(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Revoke_pending_invitation_succeeds()
    {
        var (_, invitation) = CreatePendingInvitation();

        invitation.Revoke();

        await Assert.That(invitation.Status).IsEqualTo(InvitationStatus.Revoked);
    }

    [Test]
    public async Task Revoke_accepted_invitation_throws()
    {
        var (household, invitation) = CreatePendingInvitation();
        household.AcceptInvitation(invitation, Guid.NewGuid(), Now);

        await Assert.That(() => invitation.Revoke()).Throws<InvalidOperationException>();
    }
}
