using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Application.Operations;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Lists.Application;

public sealed class ListCommandServiceTests
{
    private readonly IListRepository _repo = Substitute.For<IListRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICatalogCommands _catalogCommands = Substitute.For<ICatalogCommands>();
    private readonly ListCommandService _sut;

    public ListCommandServiceTests()
    {
        _currentUser.Id.Returns(Guid.NewGuid());
        _sut = new ListCommandService(_repo, _uow, _eventBus, new FakeTimeProvider(), _currentUser, _catalogCommands);
    }

    [Test]
    public async Task CreateListAsync_saves_list_and_returns_id()
    {
        var id = await _sut.CreateListAsync(Guid.NewGuid(), "Groceries");

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task AddItemAsync_throws_when_list_not_found()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdList?)null);

        await Assert.That(async () => await _sut.AddItemAsync(Guid.NewGuid(), "Milk", null, null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddItemAsync_adds_item_to_list()
    {
        var list = HouseholdList.Create(Guid.NewGuid(), "Groceries", _currentUser.Id, DateTimeOffset.UtcNow);
        _repo.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var itemId = await _sut.AddItemAsync(list.Id, "Milk", null, null);

        await Assert.That(itemId).IsNotEqualTo(Guid.Empty);
        await Assert.That(list.Items.Any(i => i.Name == "Milk")).IsTrue();
    }

    [Test]
    public async Task CompleteItemAsync_throws_when_list_not_found()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdList?)null);

        await Assert.That(async () => await _sut.CompleteItemAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CompleteItemAsync_marks_item_as_completed()
    {
        var list = HouseholdList.Create(Guid.NewGuid(), "Groceries", _currentUser.Id, DateTimeOffset.UtcNow);
        var item = list.AddItem("Eggs", null, null, null, null, _currentUser.Id, DateTimeOffset.UtcNow);
        list.ClearEvents();

        _repo.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await _sut.CompleteItemAsync(list.Id, item.Id);

        await Assert.That(item.IsCompleted).IsTrue();
    }

    [Test]
    public async Task UncompleteItemAsync_throws_when_list_not_found()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdList?)null);

        await Assert.That(async () => await _sut.UncompleteItemAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UncompleteItemAsync_marks_item_as_uncompleted()
    {
        var list = HouseholdList.Create(Guid.NewGuid(), "Groceries", _currentUser.Id, DateTimeOffset.UtcNow);
        var item = list.AddItem("Eggs", null, null, null, null, _currentUser.Id, DateTimeOffset.UtcNow);
        list.CompleteItem(item.Id, _currentUser.Id, DateTimeOffset.UtcNow);
        list.ClearEvents();

        _repo.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await _sut.UncompleteItemAsync(list.Id, item.Id);

        await Assert.That(item.IsCompleted).IsFalse();
    }

    [Test]
    public async Task RemoveItemAsync_throws_when_list_not_found()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdList?)null);

        await Assert.That(async () => await _sut.RemoveItemAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveItemAsync_removes_item_from_list()
    {
        var list = HouseholdList.Create(Guid.NewGuid(), "Groceries", _currentUser.Id, DateTimeOffset.UtcNow);
        var item = list.AddItem("Bread", null, null, null, null, _currentUser.Id, DateTimeOffset.UtcNow);
        list.ClearEvents();

        _repo.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await _sut.RemoveItemAsync(list.Id, item.Id);

        await Assert.That(list.Items.All(i => i.Id != item.Id)).IsTrue();
    }

    [Test]
    public async Task ChangeItemCategoryAsync_throws_when_list_not_found()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdList?)null);

        await Assert.That(async () => await _sut.ChangeItemCategoryAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ChangeItemCategoryAsync_updates_item_category()
    {
        var list = HouseholdList.Create(Guid.NewGuid(), "Groceries", _currentUser.Id, DateTimeOffset.UtcNow);
        var item = list.AddItem("Milk", null, null, null, null, _currentUser.Id, DateTimeOffset.UtcNow);
        var categoryId = Guid.NewGuid();
        list.ClearEvents();
        _repo.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await _sut.ChangeItemCategoryAsync(list.Id, item.Id, categoryId);

        await Assert.That(item.CategoryId).IsEqualTo(categoryId);
    }

    [Test]
    public async Task ChangeItemCategoryAsync_propagates_category_to_catalog()
    {
        var list = HouseholdList.Create(Guid.NewGuid(), "Groceries", _currentUser.Id, DateTimeOffset.UtcNow);
        var item = list.AddItem("Pão", null, null, null, null, _currentUser.Id, DateTimeOffset.UtcNow);
        var categoryId = Guid.NewGuid();
        list.ClearEvents();
        _repo.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await _sut.ChangeItemCategoryAsync(list.Id, item.Id, categoryId);

        await _catalogCommands.Received(1).UpsertHouseholdItemAsync(
            list.HouseholdId, "Pão", categoryId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteListAsync_calls_repo_delete()
    {
        var listId = Guid.NewGuid();

        await _sut.DeleteListAsync(listId);

        await _repo.Received(1).DeleteListAsync(listId, Arg.Any<CancellationToken>());
    }
}
