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

        var item = list.AddItem("Milk", null, null, CreatorId, Now);

        await Assert.That(item.SortOrder).IsEqualTo(1000);
    }

    [Test]
    public async Task AddItem_subsequent_item_increments_sort_order()
    {
        var list = CreateList();
        list.AddItem("Milk", null, null, CreatorId, Now);
        list.ClearEvents();

        var item = list.AddItem("Eggs", null, null, CreatorId, Now);

        await Assert.That(item.SortOrder).IsEqualTo(2000);
        await Assert.That(list.DomainEvents.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CompleteItem_marks_item_completed_and_raises_event()
    {
        var list = CreateList();
        var item = list.AddItem("Milk", null, null, CreatorId, Now);
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
        var item = list.AddItem("Milk", null, null, CreatorId, Now);
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
        var item = list.AddItem("Milk", null, null, CreatorId, Now);
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
}
