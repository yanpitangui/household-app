using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Households.Domain;

public sealed class Household : AggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<HouseholdMember> _members = [];
    public IReadOnlyList<HouseholdMember> Members => _members;

    private Household() { }

    public static Household Reconstitute(Guid id, string name, DateTimeOffset createdAt, List<HouseholdMember> members)
    {
        var h = new Household { Id = id, Name = name, CreatedAt = createdAt };
        h._members.AddRange(members);
        return h;
    }

    public static Household Create(string name, Guid ownerId, DateTimeOffset now)
    {
        var household = new Household
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            CreatedAt = now
        };
        household._members.Add(new HouseholdMember(household.Id, ownerId, HouseholdRole.Owner));
        household.Raise(new HouseholdCreated(Guid.CreateVersion7(), now, household.Id, ownerId));
        return household;
    }

    public HouseholdInvitation Invite(Guid invitedBy, DateTimeOffset now, TimeSpan expiry)
    {
        EnsureMember(invitedBy, HouseholdRole.Admin);

        var invitation = HouseholdInvitation.Create(Id, invitedBy, now, expiry);
        Raise(new HouseholdMemberInvited(Guid.CreateVersion7(), now, Id, invitation.Id));
        return invitation;
    }

    public void AcceptInvitation(HouseholdInvitation invitation, Guid userId, DateTimeOffset now)
    {
        invitation.Consume(userId, now);
        _members.Add(new HouseholdMember(Id, userId, HouseholdRole.Member));
        Raise(new HouseholdMemberJoined(Guid.CreateVersion7(), now, Id, userId, HouseholdRole.Member));
    }

    public void RemoveMember(Guid requestedBy, Guid targetUserId, DateTimeOffset now)
    {
        EnsureMember(requestedBy, HouseholdRole.Admin);
        var member = _members.FirstOrDefault(m => m.UserId == targetUserId)
            ?? throw new InvalidOperationException("User is not a member.");
        if (member.Role == HouseholdRole.Owner)
            throw new InvalidOperationException("Cannot remove the owner.");
        _members.Remove(member);
        Raise(new HouseholdMemberRemoved(Guid.CreateVersion7(), now, Id, targetUserId));
    }

    public void ChangeRole(Guid requestedBy, Guid targetUserId, HouseholdRole newRole, DateTimeOffset now)
    {
        EnsureMember(requestedBy, HouseholdRole.Owner);
        var member = _members.FirstOrDefault(m => m.UserId == targetUserId)
            ?? throw new InvalidOperationException("User is not a member.");
        if (member.Role == HouseholdRole.Owner)
            throw new InvalidOperationException("Cannot change the owner's role directly. Transfer ownership first.");
        member.SetRole(newRole);
        Raise(new HouseholdRoleChanged(Guid.CreateVersion7(), now, Id, targetUserId, newRole));
    }

    private void EnsureMember(Guid userId, HouseholdRole minimumRole)
    {
        var member = _members.FirstOrDefault(m => m.UserId == userId)
            ?? throw new InvalidOperationException("User is not a member of this household.");
        if (member.Role < minimumRole)
            throw new InvalidOperationException($"Operation requires at least {minimumRole} role.");
    }
}
