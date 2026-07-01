using HouseholdApp.Application.Modules.Catalog.Application.Ports;
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
    ICurrentUser currentUser,
    ICatalogCommands catalogCommands) : IListCommands
{
    public async Task<Guid> CreateListAsync(Guid householdId, string name, CancellationToken ct = default)
    {
        var list = HouseholdList.Create(householdId, name, currentUser.Id, time.GetUtcNow());
        await uow.BeginTransactionAsync(ct);
        await repo.SaveListAsync(list, ct);
        eventBus.EnqueueAll(list);
        await uow.CommitAsync(ct);
        return list.Id;
    }

    public async Task<Guid> AddItemAsync(Guid listId, string name, Guid? catalogItemId, Guid? categoryId, string? quantity = null, string? unit = null, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        var eventCountBefore = list.DomainEvents.Count;
        var item = list.AddItem(name, quantity, unit, catalogItemId, categoryId, currentUser.Id, time.GetUtcNow());
        var wasAdded = list.DomainEvents.Count > eventCountBefore;
        await repo.SaveListAsync(list, ct);
        eventBus.EnqueueAll(list);
        await uow.CommitAsync(ct);

        if (wasAdded)
        {
            if (catalogItemId.HasValue)
                await catalogCommands.IncrementPopularityAsync(catalogItemId.Value, ct);
            else
                await catalogCommands.UpsertHouseholdItemAsync(list.HouseholdId, name, categoryId, ct);
        }

        return item.Id;
    }

    public async Task BulkAddItemsAsync(Guid listId, IReadOnlyList<BulkAddItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        var now = time.GetUtcNow();

        var wasAdded = new bool[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var eventCountBefore = list.DomainEvents.Count;
            list.AddItem(items[i].Name, items[i].Quantity, items[i].Unit, items[i].CatalogItemId, items[i].CategoryId, currentUser.Id, now);
            wasAdded[i] = list.DomainEvents.Count > eventCountBefore;
        }

        await repo.SaveListAsync(list, ct);
        eventBus.EnqueueAll(list);
        await uow.CommitAsync(ct);

        for (var i = 0; i < items.Count; i++)
        {
            if (!wasAdded[i]) continue;
            var item = items[i];
            if (item.CatalogItemId.HasValue)
                await catalogCommands.IncrementPopularityAsync(item.CatalogItemId.Value, ct);
            else
                await catalogCommands.UpsertHouseholdItemAsync(list.HouseholdId, item.Name, item.CategoryId, ct);
        }
    }

    public async Task CompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        list.CompleteItem(itemId, currentUser.Id, time.GetUtcNow());
        await repo.SaveListAsync(list, ct);
        eventBus.EnqueueAll(list);
        await uow.CommitAsync(ct);
    }

    public async Task UncompleteItemAsync(Guid listId, Guid itemId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        list.UncompleteItem(itemId, time.GetUtcNow());
        await repo.SaveListAsync(list, ct);
        eventBus.EnqueueAll(list);
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
        eventBus.EnqueueAll(list);
        await uow.CommitAsync(ct);
    }

    public async Task ChangeItemCategoryAsync(Guid listId, Guid itemId, Guid? categoryId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");
        var item = list.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");
        list.ChangeItemCategory(itemId, categoryId, time.GetUtcNow());
        await repo.SaveListAsync(list, ct);
        eventBus.EnqueueAll(list);
        await uow.CommitAsync(ct);

        await catalogCommands.UpsertHouseholdItemAsync(list.HouseholdId, item.Name, categoryId, ct);
    }

    public async Task RemoveCompletedItemsAsync(Guid listId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var list = await repo.GetAsync(listId, ct)
            ?? throw new InvalidOperationException("List not found.");

        var completedIds = list.Items.Where(i => i.IsCompleted).Select(i => i.Id).ToList();
        if (completedIds.Count == 0)
        {
            await uow.CommitAsync(ct);
            return;
        }

        var now = time.GetUtcNow();
        foreach (var itemId in completedIds)
            list.RemoveItem(itemId, now);

        await repo.SaveListAsync(list, ct);
        foreach (var itemId in completedIds)
            await repo.DeleteItemAsync(itemId, ct);
        eventBus.EnqueueAll(list);
        await uow.CommitAsync(ct);
    }

    public async Task DeleteListAsync(Guid listId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        await repo.DeleteListAsync(listId, ct);
        await uow.CommitAsync(ct);
    }
}
