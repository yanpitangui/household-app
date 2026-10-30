using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Identity;
using Marten;
using Marten.Patching;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public sealed class ExpenseCommandService(
    IDocumentSession session,
    ICurrentUser currentUser,
    TimeProvider time) : IExpenseCommands
{
    public async Task<Guid> RecordExpenseAsync(
        Guid householdId, Guid expenseGroupId, string description, DateTimeOffset date,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default)
    {
        await EnsureActiveOwnedGroupAsync(householdId, expenseGroupId, ct);
        var expense = Expense.Record(
            householdId, expenseGroupId, description, date,
            ToDomain(fundingSources), ToDomain(allocations),
            time.GetUtcNow(), currentUser.Id);

        session.Events.Append(expense.Id, expense.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
        return expense.Id;
    }

    public async Task VoidExpenseAsync(Guid householdId, Guid expenseId, string? reason, CancellationToken ct = default)
    {
        var expense = await session.Events.AggregateStreamAsync<Expense>(expenseId, token: ct);
        if (expense is null || expense.HouseholdId != householdId)
            throw new InvalidOperationException("Expense not found.");
        await EnsureGroupNotDeletedAsync(expense.ExpenseGroupId, ct);
        expense.Void(reason, time.GetUtcNow(), currentUser.Id);
        session.Events.Append(expenseId, expense.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);
    }

    public async Task<Guid> EditExpenseAsync(
        Guid householdId, Guid expenseId, string description, DateTimeOffset date,
        IReadOnlyList<FundingSourceDto> fundingSources, IReadOnlyList<AllocationDto> allocations,
        CancellationToken ct = default)
    {
        var old = await session.Events.AggregateStreamAsync<Expense>(expenseId, token: ct);
        if (old is null || old.HouseholdId != householdId)
            throw new InvalidOperationException("Expense not found.");
        await EnsureGroupNotDeletedAsync(old.ExpenseGroupId, ct);

        var replacementId = Guid.CreateVersion7();
        old.Void("Edited", time.GetUtcNow(), currentUser.Id, correctedByExpenseId: replacementId);
        var replacement = Expense.Record(
            old.HouseholdId, old.ExpenseGroupId, description, date,
            ToDomain(fundingSources), ToDomain(allocations),
            time.GetUtcNow(), currentUser.Id, id: replacementId, correctedFromExpenseId: expenseId,
            recurringExpenseId: old.RecurringExpenseId, installment: old.Installment);

        session.Events.Append(expenseId, old.DomainEvents.ToArray());
        session.Events.Append(replacement.Id, replacement.DomainEvents.ToArray());
        await session.SaveChangesAsync(ct);

        return replacement.Id;
    }

    public async Task<Guid> RecordGroupSettlementAsync(
        Guid householdId, Guid groupId, Guid payerId, Guid recipientId, long cents, DateTimeOffset date,
        CancellationToken ct = default)
    {
        var group = await LoadOwnedGroupAsync(householdId, groupId, ct);
        if (group.DeletedAt is not null) throw new InvalidOperationException("Expense group is deleted.");

        var settlementId = Guid.CreateVersion7();
        session.Events.Append(settlementId, new SettlementRecorded(
            Guid.CreateVersion7(), time.GetUtcNow(), settlementId,
            householdId, payerId, recipientId, cents, date, currentUser.Id, ExpenseGroupId: groupId));
        await session.SaveChangesAsync(ct);
        return settlementId;
    }

    public async Task<Guid> RecordHouseholdSettlementAsync(
        Guid householdId, Guid payerId, Guid recipientId, long cents, DateTimeOffset date,
        CancellationToken ct = default)
    {
        var ledger = await session.LoadAsync<HouseholdLedger>(householdId, ct) ?? new HouseholdLedger { Id = householdId };
        var activeGroups = (await session.QueryAsync(new ExpenseGroupsForHousehold { HouseholdId = householdId }, ct))
            .Select(g => g.Id).ToHashSet();
        var split = SettlementSplitter.Split(ledger.DebtsBetween(payerId, recipientId, activeGroups), cents);
        if (split.Error is { } error) throw new SettlementSplitException(error);

        var householdSettlementId = Guid.CreateVersion7();
        var now = time.GetUtcNow();
        var groupIds = split.Legs.Select(l => l.GroupId).ToList();
        for (var i = 0; i < split.Legs.Count; i++)
        {
            var leg = split.Legs[i];
            var isLead = i == 0;
            var settlementId = isLead ? householdSettlementId : Guid.CreateVersion7();
            var (legPayer, legRecipient) = leg.IsOffset ? (recipientId, payerId) : (payerId, recipientId);
            session.Events.Append(settlementId, new SettlementRecorded(
                Guid.CreateVersion7(), now, settlementId, householdId, legPayer, legRecipient, leg.Cents, date,
                currentUser.Id,
                ExpenseGroupId: leg.GroupId,
                HouseholdSettlementId: householdSettlementId,
                IsOffset: leg.IsOffset,
                HouseholdSettlementCents: isLead ? cents : null,
                HouseholdSettlementGroupIds: isLead ? groupIds : null));
        }

        await session.SaveChangesAsync(ct);
        return householdSettlementId;
    }

    public async Task<Guid> CreateExpenseGroupAsync(
        Guid householdId, string name, string? description, string? icon = null, CancellationToken ct = default)
    {
        var group = ExpenseGroup.Create(householdId, name, description, [], time.GetUtcNow());
        var doc = new ExpenseGroupDocument
        {
            Id = group.Id, HouseholdId = group.HouseholdId, Name = group.Name, Description = group.Description,
            Icon = icon is null ? GroupIcons.Default : GroupIcons.Normalize(icon)
        };
        session.Store(doc);
        await session.SaveChangesAsync(ct);
        return group.Id;
    }

    public async Task UpdateExpenseGroupAsync(
        Guid householdId, Guid groupId, string name, string? description, string icon, CancellationToken ct = default)
    {
        var newName = ExpenseGroup.NormalizeName(name);
        var newDescription = ExpenseGroup.NormalizeDescription(description);
        var newIcon = GroupIcons.Normalize(icon);

        var group = await LoadOwnedGroupAsync(householdId, groupId, ct);
        if (group.DeletedAt is not null)
            throw new InvalidOperationException("Expense group is deleted.");

        var oldName = group.Name.Trim();
        var oldDescription = ExpenseGroup.NormalizeDescription(group.Description);
        var oldIcon = GroupIcons.Display(group.Icon);
        if (newName == oldName && newDescription == oldDescription && newIcon == oldIcon) return;

        var updated = new ExpenseGroupUpdated(
            Guid.CreateVersion7(), time.GetUtcNow(), groupId, householdId,
            oldName, newName, oldDescription, newDescription, oldIcon, newIcon, currentUser.Id);
        // Patching only the edited fields leaves a concurrent delete's DeletedAt intact.
        session.Patch<ExpenseGroupDocument>(groupId)
            .Set(x => x.Name, newName)
            .Set(x => x.Description, newDescription)
            .Set(x => x.Icon, newIcon);
        session.Events.Append(groupId, updated);
        await session.SaveChangesAsync(ct);
    }

    public async Task DeleteExpenseGroupAsync(Guid householdId, Guid groupId, CancellationToken ct = default)
    {
        var group = await LoadOwnedGroupAsync(householdId, groupId, ct);
        if (group.DeletedAt is not null) return;

        var now = time.GetUtcNow();
        group.DeletedAt = now;
        session.Store(group);
        session.Events.Append(groupId, new ExpenseGroupDeleted(
            Guid.CreateVersion7(), now, groupId, householdId, group.Name, currentUser.Id));
        await session.SaveChangesAsync(ct);
    }

    public async Task RestoreExpenseGroupAsync(Guid householdId, Guid groupId, CancellationToken ct = default)
    {
        var group = await LoadOwnedGroupAsync(householdId, groupId, ct);
        if (group.DeletedAt is null) return;

        group.DeletedAt = null;
        session.Store(group);
        session.Events.Append(groupId, new ExpenseGroupRestored(
            Guid.CreateVersion7(), time.GetUtcNow(), groupId, householdId, group.Name, currentUser.Id));
        await session.SaveChangesAsync(ct);
    }

    private async Task<ExpenseGroupDocument> LoadOwnedGroupAsync(Guid householdId, Guid groupId, CancellationToken ct)
    {
        var group = await session.LoadAsync<ExpenseGroupDocument>(groupId, ct);
        return group is not null && group.HouseholdId == householdId
            ? group
            : throw new InvalidOperationException("Expense group not found.");
    }

    private async Task EnsureActiveOwnedGroupAsync(Guid householdId, Guid groupId, CancellationToken ct)
    {
        var group = await LoadOwnedGroupAsync(householdId, groupId, ct);
        if (group.DeletedAt is not null)
            throw new InvalidOperationException("Expense group is deleted.");
    }

    // A deleted group is read-only; a missing document passes because the expense's group was validated when it was recorded.
    private async Task EnsureGroupNotDeletedAsync(Guid groupId, CancellationToken ct)
    {
        var group = await session.LoadAsync<ExpenseGroupDocument>(groupId, ct);
        if (group?.DeletedAt is not null)
            throw new InvalidOperationException("Expense group is deleted.");
    }

    private static List<FundingSource> ToDomain(IReadOnlyList<FundingSourceDto> dtos) =>
        dtos.Select(f => new FundingSource(f.UserId, f.Cents)).ToList();

    private static List<Allocation> ToDomain(IReadOnlyList<AllocationDto> dtos) =>
        dtos.Select(a => new Allocation(a.UserId, a.Cents)).ToList();
}
