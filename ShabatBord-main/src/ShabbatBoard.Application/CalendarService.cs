using ShabbatBoard.Domain;

namespace ShabbatBoard.Application;

public sealed class CalendarService(ICalendarProvider calendar, IAssignmentStore store)
{
    public async Task<CalendarYear> GetAsync(int year, CancellationToken cancellationToken)
    {
        CalendarRules.ValidateYear(year);
        var calendarTask = calendar.GetYearAsync(year, cancellationToken);
        var assignmentsTask = store.ReadAsync(cancellationToken);
        await Task.WhenAll(calendarTask, assignmentsTask);
        var byDate = (await assignmentsTask).ToDictionary(a => a.Date);
        return new(year, (await calendarTask)
            .Select(day => day with { Assignment = byDate.GetValueOrDefault(day.Date) }).ToArray());
    }
}
