using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.Web.Pages.Expenses;

/// <summary>Name, description and icon posted by the New and Edit group forms.</summary>
public sealed class GroupFormInput
{
    [Required]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>The checked quick pick.</summary>
    public string Icon { get; set; } = GroupIcons.Default;

    /// <summary>Any other emoji typed by the user; it wins over the quick pick when filled.</summary>
    public string? IconOther { get; set; }

    public string ResolvedIcon => string.IsNullOrWhiteSpace(IconOther) ? Icon : IconOther.Trim();

    public static GroupFormInput From(ExpenseGroupSummary group) => new()
    {
        Name = group.Name,
        Description = group.Description,
        Icon = group.Icon,
        IconOther = GroupIcons.QuickPicks.Contains(group.Icon) ? null : group.Icon,
    };
}

public sealed record GroupFormViewModel(GroupFormInput Form, string SubmitLabel);
