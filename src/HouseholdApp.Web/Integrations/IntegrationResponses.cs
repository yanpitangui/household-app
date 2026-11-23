using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Web.Shared.Localization;
using Microsoft.Extensions.Localization;

namespace HouseholdApp.Web.Integrations;

public sealed record IntegrationError(
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TraceId = null);

// Every response carries a message in the token owner's language, ready to be spoken or shown as-is.
public sealed class IntegrationResponses(IStringLocalizer<SharedResource> loc)
{
    public IResult Error(int status, string key) => Results.Json(new IntegrationError(loc[key].Value), statusCode: status);

    public IResult ListItems(ListItemsResult result)
    {
        if (!result.Added) return Incomplete(result.Parsed, result.Issues, result.Unparsed);

        var names = string.Join(", ", result.Items.Select(Describe));
        var message = loc["Integrations.ItemsAdded", names, result.List!.Name].Value + Ignored(result.Unparsed);
        return Results.Json(new
        {
            message,
            parsed = Parsed(result.Parsed),
            unparsed = result.Unparsed,
            list = new { id = result.List.Id, name = result.List.Name },
            items = result.Items.Select(i => new { name = i.Name, quantity = i.Quantity, unit = i.Unit, catalogMatched = i.CatalogItemId is not null }),
        }, statusCode: StatusCodes.Status201Created);
    }

    public IResult Expense(ExpenseResult result)
    {
        if (!result.Recorded) return Incomplete(result.Parsed, result.Issues, result.Unparsed);

        var amount = (result.AmountCents!.Value / 100m).ToString("C", CultureInfo.CurrentCulture);
        var message = loc["Integrations.ExpenseRecorded", amount, result.Description!, result.Group!.Name, result.Payer!.Name].Value
            + Ignored(result.Unparsed);
        return Results.Json(new
        {
            message,
            parsed = Parsed(result.Parsed),
            unparsed = result.Unparsed,
            expenseId = result.ExpenseId,
            amountCents = result.AmountCents,
            description = result.Description,
            group = new { id = result.Group.Id, name = result.Group.Name },
            payer = new { id = result.Payer.Id, name = result.Payer.Name },
        }, statusCode: StatusCodes.Status201Created);
    }

    private IResult Incomplete(
        IReadOnlyDictionary<string, ParsedValue> parsed, IReadOnlyList<IntakeIssue> issues, IReadOnlyList<string> unparsed) =>
        Results.Json(new
        {
            message = issues.Count == 0 ? loc["Integrations.Error.Unexpected"].Value : string.Join(" ", issues.Select(Explain)),
            parsed = Parsed(parsed),
            unparsed,
            issues = issues.Select(i => new { field = i.Field, reason = Camel(i.Reason.ToString()), candidates = i.Candidates }),
        }, statusCode: StatusCodes.Status422UnprocessableEntity);

    private string Explain(IntakeIssue issue)
    {
        var field = loc[$"Integrations.Field.{issue.Field}"].Value;
        var options = string.Join(", ", issue.Candidates);
        return issue.Reason switch
        {
            IssueReason.Ambiguous => loc["Integrations.Issue.Ambiguous", field, options].Value,
            IssueReason.NotFound => loc["Integrations.Issue.NotFound", field, options].Value,
            _ => loc["Integrations.Issue.Missing", field].Value,
        };
    }

    private string Ignored(IReadOnlyList<string> unparsed) =>
        unparsed.Count == 0 ? "" : " " + loc["Integrations.Ignored", string.Join(", ", unparsed)].Value;

    private static Dictionary<string, object> Parsed(IReadOnlyDictionary<string, ParsedValue> parsed) =>
        parsed.ToDictionary(kv => kv.Key, kv => (object)new { value = kv.Value.Value, source = Camel(kv.Value.Source.ToString()) });

    private static string Describe(BuiltItem item) =>
        item.Quantity is null ? item.Name : $"{item.Quantity}{(item.Unit is null ? "" : " " + item.Unit)} {item.Name}";

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}
