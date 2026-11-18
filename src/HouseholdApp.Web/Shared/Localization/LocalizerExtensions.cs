using HouseholdApp.Application.Shared.Domain;
using Microsoft.Extensions.Localization;

namespace HouseholdApp.Web.Shared.Localization;

public static class LocalizerExtensions
{
    // The message to show for a failed command: a broken business rule in the reader's language.
    public static string Explain(this IStringLocalizer localizer, InvalidOperationException ex) =>
        ex is DomainRuleException rule ? localizer[rule.Key, rule.Args].Value : ex.Message;
}
