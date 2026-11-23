using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Time;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

// Parses what a client sent, writes through the owning modules' command ports, and writes nothing until every
// field resolves.
public sealed class IntegrationIntakeService(
    IListQueries lists,
    IListCommands listCommands,
    ICatalogQueries catalog,
    IExpenseQueries expenseQueries,
    IExpenseCommands expenseCommands,
    IHouseholdQueries households,
    IHouseholdClock clock,
    ILogger<IntegrationIntakeService> logger) : IIntegrationIntake
{
    public async Task<ListItemsResult> AddListItemsAsync(IntakeContext context, ListInput input, CancellationToken ct = default)
    {
        var candidates = (await lists.ListAsync(context.HouseholdId, ct)).Select(l => new NameCandidate(l.Id, l.Name)).ToList();
        var parse = ListTextParser.Parse(input, new ListParseContext(context.DefaultListId, candidates));

        var terms = input.Items is { } given ? ListItemBuilder.SearchTerms(given) : ListItemBuilder.SearchTerms(parse.Pieces);
        var matches = terms.Count > 0
            ? await catalog.MatchIngredientsAsync(context.HouseholdId, terms, ct)
            : new Dictionary<string, CatalogItemSuggestion>();
        var built = input.Items is { } items ? ListItemBuilder.FromItems(items, matches) : ListItemBuilder.Build(parse.Pieces, matches);

        var issues = parse.Issues.ToList();
        if (built.Items.Count == 0 && issues.All(i => i.Field != IntakeFields.Items))
            issues.Add(new IntakeIssue(IntakeFields.Items, IssueReason.Missing, []));

        if (issues.Count == 0)
        {
            try
            {
                await listCommands.BulkAddItemsAsync(parse.List!.Id,
                    built.Items.Select(i => new BulkAddItem(i.Name, i.Quantity, i.Unit, i.CatalogItemId, i.CategoryId)).ToList(), ct);
            }
            catch (InvalidOperationException ex) when (ex is not DomainRuleException)
            {
                // The list disappeared between matching and writing; report it like any unmatched list.
                issues.Add(new IntakeIssue(IntakeFields.List, IssueReason.NotFound, candidates.Select(c => c.Name).ToList()));
            }
        }

        var result = new ListItemsResult(parse.List, built.Items, parse.Parsed, issues, built.Unparsed);
        IntegrationsLog.Handled(logger, "list-items", context.HouseholdId, context.TokenId,
            result.Added ? "added" : "incomplete", IssueCodes(issues),
            input.Text ?? string.Join(", ", input.Items?.Select(i => i.Name) ?? []));
        return result;
    }

    public async Task<ExpenseResult> RecordExpenseAsync(IntakeContext context, ExpenseInput input, CancellationToken ct = default)
    {
        var members = (await households.GetMembersAsync(context.HouseholdId, ct)).Select(m => new NameCandidate(m.UserId, m.DisplayName)).ToList();
        if (members.All(m => m.Id != context.UserId))
        {
            var notMember = new ExpenseResult(null, null, null, null, null, new Dictionary<string, ParsedValue>(),
                [new IntakeIssue(IntakeFields.Payer, IssueReason.NotFound, members.Select(m => m.Name).ToList())], []);
            IntegrationsLog.Handled(logger, "expenses", context.HouseholdId, context.TokenId, "incomplete",
                IssueCodes(notMember.Issues), input.Text ?? input.Description ?? "");
            return notMember;
        }
        var groups = (await expenseQueries.ListExpenseGroupsAsync(context.HouseholdId, ct)).Select(g => new NameCandidate(g.Id, g.Name)).ToList();
        var parse = ExpenseTextParser.Parse(input,
            new ExpenseParseContext(context.UserId, context.DefaultGroupId, members, groups, context.Culture));

        Guid? expenseId = null;
        if (parse.Issues.Count == 0)
        {
            var cents = parse.AmountCents!.Value;
            var payer = parse.Payer!.Id;
            var today = await clock.TodayAsync(context.HouseholdId, ct);
            expenseId = await expenseCommands.RecordExpenseAsync(
                context.HouseholdId, parse.Group!.Id, parse.Description!,
                new DateTimeOffset(today, TimeOnly.MinValue, TimeSpan.Zero),
                [new FundingSourceDto(payer, cents)],
                EqualSplit.Allocate(cents, members.Select(m => m.Id).ToList(), payer),
                ct);
        }

        var result = new ExpenseResult(expenseId, parse.AmountCents, parse.Description, parse.Group, parse.Payer,
            parse.Parsed, parse.Issues, parse.Unparsed);
        IntegrationsLog.Handled(logger, "expenses", context.HouseholdId, context.TokenId,
            result.Recorded ? "recorded" : "incomplete", IssueCodes(parse.Issues), input.Text ?? input.Description ?? "");
        return result;
    }

    private static string IssueCodes(IReadOnlyList<IntakeIssue> issues) =>
        string.Join(",", issues.Select(i => $"{i.Field}:{i.Reason}"));
}
