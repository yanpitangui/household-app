using System.Globalization;
using HouseholdApp.Application.Shared.Domain;
using Microsoft.Extensions.Localization;

namespace HouseholdApp.Web.Shared.Web;

/// <summary>Field names are <c>{Prefix}Frequency</c>, <c>{Prefix}FirstDate</c> and <c>{Prefix}Time</c>.</summary>
public sealed record RecurrencePickerViewModel(string Prefix, RecurrenceFrequency Frequency, DateOnly FirstDate, TimeOnly Time);

public static class RecurrenceLabel
{
    public static string Describe(RecurrenceSchedule schedule, IStringLocalizer localizer)
    {
        var culture = CultureInfo.CurrentCulture;
        var time = schedule.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        var date = schedule.FirstDate;
        return schedule.Frequency switch
        {
            RecurrenceFrequency.Daily => localizer["Recurrence.Describe.Daily", time].Value,
            RecurrenceFrequency.Weekly => localizer["Recurrence.Describe.Weekly", culture.DateTimeFormat.GetDayName(date.DayOfWeek), time].Value,
            RecurrenceFrequency.Biweekly => localizer["Recurrence.Describe.Biweekly", culture.DateTimeFormat.GetDayName(date.DayOfWeek), time].Value,
            RecurrenceFrequency.Monthly => localizer["Recurrence.Describe.Monthly", date.Day, time].Value,
            RecurrenceFrequency.Annually => localizer["Recurrence.Describe.Annually", date.ToString("dd MMM", culture), time].Value,
            _ => throw new ArgumentOutOfRangeException(nameof(schedule))
        };
    }
}
