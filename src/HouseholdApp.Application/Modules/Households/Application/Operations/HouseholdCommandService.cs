using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

public sealed class HouseholdCommandService(
    IHouseholdRepository repo,
    IUnitOfWork uow,
    IEventBus eventBus,
    TimeProvider time,
    ICurrentUser currentUser) : IHouseholdCommands
{
    private static readonly TimeSpan InvitationExpiry = TimeSpan.FromDays(7);

    public async Task<Guid> CreateAsync(string name, string timeZone, CancellationToken ct = default)
    {
        var household = Household.Create(name, currentUser.Id, timeZone, time.GetUtcNow());
        await uow.BeginTransactionAsync(ct);
        await repo.InsertAsync(household, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
        return household.Id;
    }

    public async Task RenameAsync(Guid householdId, string name, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var household = await LoadAsync(householdId, ct);
        household.Rename(currentUser.Id, name, time.GetUtcNow());
        await repo.RenameAsync(householdId, household.Name, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
    }

    public async Task ChangeTimeZoneAsync(Guid householdId, string timeZone, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var household = await LoadAsync(householdId, ct);
        household.ChangeTimeZone(currentUser.Id, timeZone, time.GetUtcNow());
        await repo.SetTimeZoneAsync(householdId, household.TimeZone, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
    }

    public async Task<string> InviteAsync(Guid householdId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var household = await LoadAsync(householdId, ct);
        var invitation = household.Invite(currentUser.Id, time.GetUtcNow(), InvitationExpiry);
        await repo.SaveInvitationAsync(invitation, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
        return invitation.Token;
    }

    public async Task<bool> AcceptInvitationAsync(string token, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);

        // Fetch invitation while still Pending so the domain Consume() check passes.
        // The atomic SQL update below is the real single-use enforcement.
        var invitation = await repo.GetInvitationByTokenAsync(token, ct);
        if (invitation is null) return false;

        var now = time.GetUtcNow();
        var userId = currentUser.Id;
        var consumed = await repo.ConsumeInvitationAtomicAsync(token, userId, now, ct);
        if (!consumed) return false;

        var household = await LoadAsync(invitation.HouseholdId, ct);

        household.AcceptInvitation(invitation, userId, now);
        await repo.AddMemberAsync(household.Id, userId, HouseholdRole.Member, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
        return true;
    }

    public async Task RevokeInvitationAsync(Guid householdId, Guid invitationId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var invitation = await repo.GetInvitationAsync(invitationId, ct);
        if (invitation is null || invitation.HouseholdId != householdId)
            throw new InvalidOperationException("Invitation not found.");
        invitation.Revoke();
        await repo.SaveInvitationAsync(invitation, ct);
        await uow.CommitAsync(ct);
    }

    public async Task RemoveMemberAsync(Guid householdId, Guid targetUserId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var household = await LoadAsync(householdId, ct);
        household.RemoveMember(currentUser.Id, targetUserId, time.GetUtcNow());
        await repo.RemoveMemberAsync(householdId, targetUserId, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
    }

    public async Task LeaveAsync(Guid householdId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var household = await LoadAsync(householdId, ct);
        household.Leave(currentUser.Id, time.GetUtcNow());
        await repo.RemoveMemberAsync(householdId, currentUser.Id, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
    }

    public async Task ChangeMemberRoleAsync(Guid householdId, Guid targetUserId, string newRole, CancellationToken ct = default)
    {
        if (!Enum.TryParse<HouseholdRole>(newRole, ignoreCase: true, out var role))
            throw new ArgumentException($"Unknown role: {newRole}");

        await uow.BeginTransactionAsync(ct);
        var household = await LoadAsync(householdId, ct);
        household.ChangeRole(currentUser.Id, targetUserId, role, time.GetUtcNow());
        await repo.SetRoleAsync(householdId, targetUserId, role, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
    }

    public async Task TransferOwnershipAsync(Guid householdId, Guid newOwnerId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var household = await LoadAsync(householdId, ct);
        household.TransferOwnership(currentUser.Id, newOwnerId, time.GetUtcNow());
        await repo.SetRoleAsync(householdId, newOwnerId, HouseholdRole.Owner, ct);
        await repo.SetRoleAsync(householdId, currentUser.Id, HouseholdRole.Admin, ct);
        eventBus.EnqueueAll(household);
        await uow.CommitAsync(ct);
    }

    private async Task<Household> LoadAsync(Guid householdId, CancellationToken ct) =>
        await repo.GetAsync(householdId, ct) ?? throw new InvalidOperationException("Household not found.");
}
