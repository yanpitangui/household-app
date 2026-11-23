using System.Globalization;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Time;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class IntegrationIntakeServiceTests
{
    private static readonly Guid Household = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Leo = Guid.NewGuid();
    private static readonly Guid General = Guid.NewGuid();
    private static readonly Guid Trip = Guid.NewGuid();
    private static readonly Guid Groceries = Guid.NewGuid();
    private static readonly Guid Pharmacy = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 23);

    private readonly IListQueries _lists = Substitute.For<IListQueries>();
    private readonly IListCommands _listCommands = Substitute.For<IListCommands>();
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();
    private readonly IExpenseQueries _expenseQueries = Substitute.For<IExpenseQueries>();
    private readonly IExpenseCommands _expenseCommands = Substitute.For<IExpenseCommands>();
    private readonly IHouseholdQueries _households = Substitute.For<IHouseholdQueries>();
    private readonly IHouseholdClock _clock = Substitute.For<IHouseholdClock>();

    private IntegrationIntakeService CreateSut()
    {
        _households.GetMembersAsync(Household, Arg.Any<CancellationToken>()).Returns(
            [new HouseholdMemberDto(Ana, "Ana Souza", "Owner", null), new HouseholdMemberDto(Leo, "Leo Lima", "Member", null)]);
        _expenseQueries.ListExpenseGroupsAsync(Household, Arg.Any<CancellationToken>()).Returns(
            [new ExpenseGroupSummary(General, "General", null), new ExpenseGroupSummary(Trip, "Trip", null)]);
        _lists.ListAsync(Household, Arg.Any<CancellationToken>()).Returns(
            [new ListSummary(Groceries, "Groceries", 0, 0), new ListSummary(Pharmacy, "Pharmacy", 0, 0)]);
        _catalog.MatchIngredientsAsync(Household, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CatalogItemSuggestion>());
        _clock.TodayAsync(Household, Arg.Any<CancellationToken>()).Returns(Today);
        _expenseCommands.RecordExpenseAsync(default, default, default!, default, default!, default!, default)
            .ReturnsForAnyArgs(Guid.NewGuid());
        return new IntegrationIntakeService(_lists, _listCommands, _catalog, _expenseQueries, _expenseCommands,
            _households, _clock, NullLogger<IntegrationIntakeService>.Instance);
    }

    private static IntakeContext Ctx() =>
        new(Household, Ana, Guid.NewGuid(), null, null, CultureInfo.GetCultureInfo("pt-BR"));

    [Test]
    public async Task Records_a_spoken_expense_split_equally_with_the_remainder_to_the_payer()
    {
        var result = await CreateSut().RecordExpenseAsync(Ctx(), new ExpenseInput("50,01 mercado no trip pago por Leo", null, null, null, null));

        await Assert.That(result.Recorded).IsTrue();
        await _expenseCommands.Received(1).RecordExpenseAsync(
            Household, Trip, "mercado", new DateTimeOffset(Today, TimeOnly.MinValue, TimeSpan.Zero),
            Arg.Is<IReadOnlyList<FundingSourceDto>>(f => f.Single().UserId == Leo && f.Single().Cents == 5001),
            Arg.Is<IReadOnlyList<AllocationDto>>(a =>
                a.Single(x => x.UserId == Ana).Cents == 2500 && a.Single(x => x.UserId == Leo).Cents == 2501),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Does_not_record_while_something_is_missing()
    {
        var result = await CreateSut().RecordExpenseAsync(Ctx(), new ExpenseInput("50 pizza", null, null, null, null));

        await Assert.That(result.Recorded).IsFalse();
        await Assert.That(result.Issues.Single().Field).IsEqualTo(IntakeFields.Group);
        await _expenseCommands.DidNotReceiveWithAnyArgs()
            .RecordExpenseAsync(default, default, default!, default, default!, default!, default);
    }

    [Test]
    public async Task Adds_spoken_items_to_the_named_list()
    {
        var result = await CreateSut().AddListItemsAsync(Ctx(), new ListInput("add milk, eggs and 2 kg rice to groceries", null, null));

        await Assert.That(result.Added).IsTrue();
        await _listCommands.Received(1).BulkAddItemsAsync(
            Groceries,
            Arg.Is<IReadOnlyList<BulkAddItem>>(items =>
                items.Select(i => i.Name).SequenceEqual(new[] { "milk", "eggs", "rice" })
                && items[2].Quantity == "2" && items[2].Unit == "kg"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Adds_structured_items_without_splitting()
    {
        var result = await CreateSut().AddListItemsAsync(Ctx(),
            new ListInput(null, [new ListItemInput("arroz e feijão", "1", null)], "pharmacy"));

        await Assert.That(result.Added).IsTrue();
        await _listCommands.Received(1).BulkAddItemsAsync(
            Pharmacy, Arg.Is<IReadOnlyList<BulkAddItem>>(items => items.Single().Name == "arroz e feijão"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Does_not_add_items_while_the_list_is_unclear()
    {
        var result = await CreateSut().AddListItemsAsync(Ctx(), new ListInput("milk", null, null));

        await Assert.That(result.Added).IsFalse();
        await _listCommands.DidNotReceiveWithAnyArgs().BulkAddItemsAsync(default, default!, default);
    }

    [Test]
    public async Task Does_not_record_when_the_token_owner_is_not_a_member()
    {
        var sut = CreateSut();
        var outsider = Ctx() with { UserId = Guid.NewGuid() };

        var result = await sut.RecordExpenseAsync(outsider, new ExpenseInput("50 mercado no trip", null, null, null, null));

        await Assert.That(result.Recorded).IsFalse();
        await Assert.That(result.Issues.Single().Field).IsEqualTo(IntakeFields.Payer);
        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.NotFound);
        await _expenseCommands.DidNotReceiveWithAnyArgs()
            .RecordExpenseAsync(default, default, default!, default, default!, default!, default);
    }

    [Test]
    public async Task Does_not_record_when_the_household_has_no_members()
    {
        var sut = CreateSut();
        _households.GetMembersAsync(Household, Arg.Any<CancellationToken>()).Returns(Array.Empty<HouseholdMemberDto>());

        var result = await sut.RecordExpenseAsync(Ctx(), new ExpenseInput("50 mercado no trip", null, null, null, null));

        await Assert.That(result.Recorded).IsFalse();
        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.NotFound);
    }

    [Test]
    public async Task Reports_the_list_when_it_is_deleted_between_matching_and_writing()
    {
        var sut = CreateSut();
        _listCommands.BulkAddItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<BulkAddItem>>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("List not found.")));

        var result = await sut.AddListItemsAsync(Ctx(), new ListInput("milk to groceries", null, null));

        await Assert.That(result.Added).IsFalse();
        await Assert.That(result.Issues.Single().Field).IsEqualTo(IntakeFields.List);
        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.NotFound);
    }

    [Test]
    public async Task Ignores_a_default_list_from_another_household()
    {
        var result = await CreateSut().AddListItemsAsync(Ctx() with { DefaultListId = Guid.NewGuid() }, new ListInput("milk", null, null));

        await Assert.That(result.Issues.Single().Field).IsEqualTo(IntakeFields.List);
        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.Missing);
        await _listCommands.DidNotReceiveWithAnyArgs().BulkAddItemsAsync(default, default!, default);
    }

    [Test]
    public async Task Ignores_a_default_group_from_another_household()
    {
        var result = await CreateSut().RecordExpenseAsync(Ctx() with { DefaultGroupId = Guid.NewGuid() }, new ExpenseInput("50 pizza", null, null, null, null));

        await Assert.That(result.Issues.Single().Field).IsEqualTo(IntakeFields.Group);
        await Assert.That(result.Issues.Single().Reason).IsEqualTo(IssueReason.Missing);
    }
}
