using HouseholdApp.Application.Modules.Catalog.Application.Operations;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Catalog.Domain;
using HouseholdApp.Application.Shared.Events;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Catalog.Application;

public sealed class CatalogCommandServiceTests
{
    private readonly ICatalogRepository _repo = Substitute.For<ICatalogRepository>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly CatalogCommandService _sut;

    public CatalogCommandServiceTests()
    {
        _sut = new CatalogCommandService(_repo, _eventBus, new FakeTimeProvider());
    }

    [Test]
    public async Task AddHouseholdCategoryAsync_enqueues_CategoryAdded_and_flushes()
    {
        var householdId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        _repo.AddHouseholdCategoryAsync(householdId, "Fruits", "🍎", Arg.Any<CancellationToken>()).Returns(categoryId);

        var result = await _sut.AddHouseholdCategoryAsync(householdId, "Fruits", "🍎");

        await Assert.That(result).IsEqualTo(categoryId);
        _eventBus.Received(1).Enqueue(Arg.Is<CategoryAdded>(e => e.HouseholdId == householdId && e.CategoryId == categoryId));
        await _eventBus.Received(1).FlushDeferredAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateHouseholdCategoryAsync_enqueues_CategoryUpdated_and_flushes()
    {
        var householdId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await _sut.UpdateHouseholdCategoryAsync(householdId, categoryId, "Veggies", "🥕");

        await _repo.Received(1).UpdateHouseholdCategoryAsync(householdId, categoryId, "Veggies", "🥕", Arg.Any<CancellationToken>());
        _eventBus.Received(1).Enqueue(Arg.Is<CategoryUpdated>(e => e.HouseholdId == householdId && e.CategoryId == categoryId));
        await _eventBus.Received(1).FlushDeferredAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteHouseholdCategoryAsync_enqueues_CategoryDeleted_and_flushes()
    {
        var householdId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await _sut.DeleteHouseholdCategoryAsync(householdId, categoryId);

        await _repo.Received(1).DeleteHouseholdCategoryAsync(householdId, categoryId, Arg.Any<CancellationToken>());
        _eventBus.Received(1).Enqueue(Arg.Is<CategoryDeleted>(e => e.HouseholdId == householdId && e.CategoryId == categoryId));
        await _eventBus.Received(1).FlushDeferredAsync(Arg.Any<CancellationToken>());
    }
}
