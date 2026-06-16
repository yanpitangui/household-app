using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Lists.Application.Operations;

public sealed class ListCommandService(
    IListRepository repo,
    IUnitOfWork uow,
    IEventBus eventBus,
    TimeProvider time,
    ICurrentUser currentUser) : IListCommands
{
    public async Task<Guid> CreateListAsync(Guid householdId, string name, CancellationToken ct = default)
    {
        var list = HouseholdList.Create(householdId, name, currentUser.Id, time.GetUtcNow());
        await uow.BeginTransactionAsync(ct);
        await repo.SaveListAsync(list, ct);
        await eventBus.PublishAllAsync(list, ct);
        await uow.CommitAsync(ct);
        return list.Id;
    }

    public async Task<Guid> AddItemAsync(Guid listId, string name, string? category, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        var item = list.AddItem(name, category, time.GetUtcNow());
        await repo.SaveListAsync(list, ct);
        await eventBus.PublishAllAsync(list, ct);
        await uow.CommitAsync(ct);
        return item.Id;
    }

    public async Task CompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        list.CompleteItem(itemId, currentUser.Id, time.GetUtcNow());
        await repo.SaveListAsync(list, ct);
        await eventBus.PublishAllAsync(list, ct);
        await uow.CommitAsync(ct);
    }

    public async Task UncompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        list.UncompleteItem(itemId, time.GetUtcNow());
        await repo.SaveListAsync(list, ct);
        await eventBus.PublishAllAsync(list, ct);
        await uow.CommitAsync(ct);
    }

    public async Task RemoveItemAsync(Guid listId, Guid itemId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        list.RemoveItem(itemId, time.GetUtcNow());
        await repo.SaveListAsync(list, ct);
        await repo.DeleteItemAsync(itemId, ct);
        await eventBus.PublishAllAsync(list, ct);
        await uow.CommitAsync(ct);
    }

    public async Task DeleteListAsync(Guid listId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        await repo.DeleteListAsync(listId, ct);
        await uow.CommitAsync(ct);
    }
}
