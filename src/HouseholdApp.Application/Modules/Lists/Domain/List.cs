using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Lists.Domain;

public sealed class ListItem
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public string Name { get; set; } = default!;
    public Guid? CatalogItemId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid AddedBy { get; set; }
    public int SortOrder { get; set; }
    public bool IsCompleted { get; set; }

    internal ListItem(Guid listId, string name, Guid? catalogItemId, Guid? categoryId, Guid addedBy, int sortOrder)
    {
        Id = Guid.CreateVersion7();
        ListId = listId;
        Name = name;
        CatalogItemId = catalogItemId;
        CategoryId = categoryId;
        AddedBy = addedBy;
        SortOrder = sortOrder;
    }

    public ListItem() { }

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

    private readonly List<ListItem> _items = [];
    public IReadOnlyList<ListItem> Items => _items;

    private HouseholdList() { }

    public static HouseholdList Reconstitute(Guid id, Guid householdId, string name, Guid createdBy, DateTimeOffset createdAt, List<ListItem> items)
    {
        var list = new HouseholdList { Id = id, HouseholdId = householdId, Name = name, CreatedBy = createdBy, CreatedAt = createdAt };
        list._items.AddRange(items);
        return list;
    }

    public static HouseholdList Create(Guid householdId, string name, Guid createdBy, DateTimeOffset now)
    {
        var list = new HouseholdList
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            Name = name,
            CreatedBy = createdBy,
            CreatedAt = now
        };
        list.Raise(new ListCreated(Guid.CreateVersion7(), now, list.Id, householdId, name, createdBy));
        return list;
    }

    public ListItem AddItem(string name, Guid? catalogItemId, Guid? categoryId, Guid addedBy, DateTimeOffset now)
    {
        var sortOrder = _items.Count == 0 ? 1000 : _items.Max(i => i.SortOrder) + 1000;
        var item = new ListItem(Id, name, catalogItemId, categoryId, addedBy, sortOrder);
        _items.Add(item);
        Raise(new ListItemAdded(Guid.CreateVersion7(), now, Id, item.Id, name, catalogItemId, categoryId, addedBy, sortOrder));
        return item;
    }

    public void CompleteItem(Guid itemId, Guid completedBy, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");
        item.Complete();
        Raise(new ListItemCompleted(Guid.CreateVersion7(), now, Id, itemId, completedBy));
    }

    public void UncompleteItem(Guid itemId, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");
        item.Uncomplete();
        Raise(new ListItemUncompleted(Guid.CreateVersion7(), now, Id, itemId));
    }

    public void RemoveItem(Guid itemId, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");
        _items.Remove(item);
        Raise(new ListItemRemoved(Guid.CreateVersion7(), now, Id, itemId));
    }
}
