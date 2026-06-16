using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Lists.Domain;

public sealed class ShoppingItem
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public string Name { get; set; } = default!;
    public string? Category { get; set; }
    public int SortOrder { get; set; }
    public bool IsCompleted { get; set; }

    internal ShoppingItem(Guid listId, string name, string? category, int sortOrder)
    {
        Id = Guid.NewGuid();
        ListId = listId;
        Name = name;
        Category = category;
        SortOrder = sortOrder;
    }

    public ShoppingItem() { }

    internal void Complete() => IsCompleted = true;
    internal void Uncomplete() => IsCompleted = false;
}

public sealed class HouseholdList : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = default!;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<ShoppingItem> _items = [];
    public IReadOnlyList<ShoppingItem> Items => _items;

    private HouseholdList() { }

    public static HouseholdList Reconstitute(Guid id, Guid householdId, string name, Guid createdBy, DateTimeOffset createdAt, List<ShoppingItem> items)
    {
        var list = new HouseholdList { Id = id, HouseholdId = householdId, Name = name, CreatedBy = createdBy, CreatedAt = createdAt };
        list._items.AddRange(items);
        return list;
    }

    public static HouseholdList Create(Guid householdId, string name, Guid createdBy, DateTimeOffset now)
    {
        var list = new HouseholdList
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Name = name,
            CreatedBy = createdBy,
            CreatedAt = now
        };
        list.Raise(new ListCreated(Guid.NewGuid(), now, list.Id, householdId, name, createdBy));
        return list;
    }

    public ShoppingItem AddItem(string name, string? category, DateTimeOffset now)
    {
        var sortOrder = _items.Count == 0 ? 1000 : _items.Max(i => i.SortOrder) + 1000;
        var item = new ShoppingItem(Id, name, category, sortOrder);
        _items.Add(item);
        Raise(new ListItemAdded(Guid.NewGuid(), now, Id, item.Id, name, category, sortOrder));
        return item;
    }

    public void CompleteItem(Guid itemId, Guid completedBy, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");
        item.Complete();
        Raise(new ListItemCompleted(Guid.NewGuid(), now, Id, itemId, completedBy));
    }

    public void UncompleteItem(Guid itemId, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");
        item.Uncomplete();
        Raise(new ListItemUncompleted(Guid.NewGuid(), now, Id, itemId));
    }

    public void RemoveItem(Guid itemId, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");
        _items.Remove(item);
        Raise(new ListItemRemoved(Guid.NewGuid(), now, Id, itemId));
    }
}
