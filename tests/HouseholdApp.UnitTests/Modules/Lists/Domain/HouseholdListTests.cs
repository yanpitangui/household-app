using HouseholdApp.Application.Modules.Lists.Domain;

namespace HouseholdApp.UnitTests.Modules.Lists.Domain;

public sealed class HouseholdListTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static HouseholdList CreateList() =>
        HouseholdList.Create(HouseholdId, "Weekly Shop", CreatorId, Now);

    [Test]
    public async Task Create_sets_properties_and_raises_event()
    {
        var list = CreateList();

        await Assert.That(list.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(list.Name).IsEqualTo("Weekly Shop");
        await Assert.That(list.CreatedBy).IsEqualTo(CreatorId);
        await Assert.That(list.Items).IsEmpty();
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(list.DomainEvents[0] is ListCreated).IsTrue();
    }

    [Test]
    public async Task AddItem_first_item_gets_sort_order_1000()
    {
        var list = CreateList();

        var item = list.AddItem("Milk", null, null, null, null, CreatorId, Now);

        await Assert.That(item.SortOrder).IsEqualTo(1000);
    }

    [Test]
    public async Task AddItem_subsequent_item_increments_sort_order()
    {
        var list = CreateList();
        list.AddItem("Milk", null, null, null, null, CreatorId, Now);
        list.ClearEvents();

        var item = list.AddItem("Eggs", null, null, null, null, CreatorId, Now);

        await Assert.That(item.SortOrder).IsEqualTo(2000);
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddItem_duplicate_name_and_category_returns_existing_item_without_raising_event()
    {
        var categoryId = Guid.NewGuid();
        var list = CreateList();
        var first = list.AddItem("Milk", null, null, null, categoryId, CreatorId, Now);
        list.ClearEvents();

        var second = list.AddItem("milk", "2", "L", null, categoryId, CreatorId, Now);

        await Assert.That(second.Id).IsEqualTo(first.Id);
        await Assert.That(list.Items.Count).IsEqualTo(1);
        await Assert.That(list.DomainEvents).IsEmpty();
    }

    [Test]
    public async Task AddItem_same_name_different_category_adds_new_item()
    {
        var list = CreateList();
        list.AddItem("Milk", null, null, null, Guid.NewGuid(), CreatorId, Now);
        list.ClearEvents();

        var second = list.AddItem("Milk", null, null, null, Guid.NewGuid(), CreatorId, Now);

        await Assert.That(list.Items.Count).IsEqualTo(2);
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddItem_same_name_and_category_but_existing_is_completed_adds_new_item()
    {
        var categoryId = Guid.NewGuid();
        var list = CreateList();
        var first = list.AddItem("Milk", null, null, null, categoryId, CreatorId, Now);
        list.CompleteItem(first.Id, CreatorId, Now);
        list.ClearEvents();

        var second = list.AddItem("Milk", null, null, null, categoryId, CreatorId, Now);

        await Assert.That(second.Id).IsNotEqualTo(first.Id);
        await Assert.That(list.Items.Count).IsEqualTo(2);
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CompleteItem_marks_item_completed_and_raises_event()
    {
        var list = CreateList();
        var item = list.AddItem("Milk", null, null, null, null, CreatorId, Now);
        list.ClearEvents();

        list.CompleteItem(item.Id, CreatorId, Now);

        await Assert.That(item.IsCompleted).IsTrue();
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(list.DomainEvents[0] is ListItemCompleted).IsTrue();
    }

    [Test]
    public async Task CompleteItem_unknown_id_throws()
    {
        var list = CreateList();

        await Assert.That(() =>
            list.CompleteItem(Guid.NewGuid(), CreatorId, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UncompleteItem_marks_item_uncompleted_and_raises_event()
    {
        var list = CreateList();
        var item = list.AddItem("Milk", null, null, null, null, CreatorId, Now);
        list.CompleteItem(item.Id, CreatorId, Now);
        list.ClearEvents();

        list.UncompleteItem(item.Id, Now);

        await Assert.That(item.IsCompleted).IsFalse();
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(list.DomainEvents[0] is ListItemUncompleted).IsTrue();
    }

    [Test]
    public async Task UncompleteItem_unknown_id_throws()
    {
        var list = CreateList();

        await Assert.That(() =>
            list.UncompleteItem(Guid.NewGuid(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveItem_removes_from_list_and_raises_event()
    {
        var list = CreateList();
        var item = list.AddItem("Milk", null, null, null, null, CreatorId, Now);
        list.ClearEvents();

        list.RemoveItem(item.Id, Now);

        await Assert.That(list.Items).IsEmpty();
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(list.DomainEvents[0] is ListItemRemoved).IsTrue();
    }

    [Test]
    public async Task RemoveItem_unknown_id_throws()
    {
        var list = CreateList();

        await Assert.That(() =>
            list.RemoveItem(Guid.NewGuid(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ChangeItemCategory_sets_category_and_raises_event()
    {
        var list = CreateList();
        var item = list.AddItem("Milk", null, null, null, null, CreatorId, Now);
        var categoryId = Guid.NewGuid();
        list.ClearEvents();

        list.ChangeItemCategory(item.Id, categoryId, Now);

        await Assert.That(item.CategoryId).IsEqualTo(categoryId);
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(list.DomainEvents[0] is ListItemCategoryChanged).IsTrue();
    }

    [Test]
    public async Task ChangeItemCategory_clears_category_when_null()
    {
        var list = CreateList();
        var item = list.AddItem("Milk", null, null, null, Guid.NewGuid(), CreatorId, Now);
        list.ClearEvents();

        list.ChangeItemCategory(item.Id, null, Now);

        await Assert.That(item.CategoryId).IsNull();
    }

    [Test]
    public async Task ChangeItemCategory_unknown_id_throws()
    {
        var list = CreateList();

        await Assert.That(() =>
            list.ChangeItemCategory(Guid.NewGuid(), Guid.NewGuid(), Now))
            .Throws<InvalidOperationException>();
    }
}
