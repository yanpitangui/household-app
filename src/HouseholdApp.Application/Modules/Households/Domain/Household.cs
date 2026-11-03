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

    public string TimeZone { get; private set; } = HouseholdTimeZone.Default;

    public static Household Reconstitute(Guid id, string name, string timeZone, DateTimeOffset createdAt, List<HouseholdMember> members)
    {
        var h = new Household { Id = id, Name = name, TimeZone = timeZone, CreatedAt = createdAt };
        h._members.AddRange(members);
        return h;
    }

    public static Household Create(string name, Guid ownerId, string timeZone, DateTimeOffset now)
    {
        var household = new Household
        {
            Id = Guid.CreateVersion7(),
            Name = RequireName(name),
            TimeZone = HouseholdTimeZone.IsValid(timeZone) ? timeZone : HouseholdTimeZone.Default,
            CreatedAt = now
        };
        household._members.Add(new HouseholdMember(household.Id, ownerId, HouseholdRole.Owner));
        household.Raise(new HouseholdCreated(Guid.CreateVersion7(), now, household.Id, ownerId));
        return household;
    }

    public void Rename(Guid requestedBy, string name, DateTimeOffset now)
    {
        EnsureMember(requestedBy, HouseholdRole.Admin);
        Name = RequireName(name);
        Raise(new HouseholdRenamed(Guid.CreateVersion7(), now, Id, Name, _members.Select(m => m.UserId).ToList()));
    }

    public void ChangeTimeZone(Guid requestedBy, string timeZone, DateTimeOffset now)
    {
        EnsureMember(requestedBy, HouseholdRole.Admin);
        if (!HouseholdTimeZone.IsValid(timeZone))
            throw new ArgumentException($"Unknown time zone: {timeZone}", nameof(timeZone));
        TimeZone = timeZone;
        Raise(new HouseholdTimeZoneChanged(Guid.CreateVersion7(), now, Id, timeZone));
    }

    public void TransferOwnership(Guid requestedBy, Guid newOwnerId, DateTimeOffset now)
    {
        EnsureMember(requestedBy, HouseholdRole.Owner);
        var target = _members.FirstOrDefault(m => m.UserId == newOwnerId)
            ?? throw new InvalidOperationException("User is not a member.");
        if (target.UserId == requestedBy)
            throw new InvalidOperationException("You already own this household.");
        var owner = _members.Single(m => m.UserId == requestedBy);

        target.SetRole(HouseholdRole.Owner);
        owner.SetRole(HouseholdRole.Admin);
        Raise(new HouseholdRoleChanged(Guid.CreateVersion7(), now, Id, newOwnerId, HouseholdRole.Owner));
        Raise(new HouseholdRoleChanged(Guid.CreateVersion7(), now, Id, requestedBy, HouseholdRole.Admin));
    }

    public void Leave(Guid userId, DateTimeOffset now)
    {
        var member = _members.FirstOrDefault(m => m.UserId == userId)
            ?? throw new InvalidOperationException("User is not a member of this household.");
        if (member.Role == HouseholdRole.Owner)
            throw new InvalidOperationException("Transfer ownership before leaving.");
        _members.Remove(member);
        Raise(new HouseholdMemberRemoved(Guid.CreateVersion7(), now, Id, userId));
    }

    /// <summary>Whether a member with this role can be removed by someone else; the Owner leaves only after a transfer.</summary>
    public static bool CanBeRemoved(HouseholdRole role) => role != HouseholdRole.Owner;

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A household needs a name.", nameof(name)) : name.Trim();

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
        if (targetUserId == requestedBy)
            throw new InvalidOperationException("Use Leave to leave a household.");
        var member = _members.FirstOrDefault(m => m.UserId == targetUserId)
            ?? throw new InvalidOperationException("User is not a member.");
        if (!CanBeRemoved(member.Role))
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
