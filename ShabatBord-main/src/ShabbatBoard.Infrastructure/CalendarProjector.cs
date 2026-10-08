using ShabbatBoard.Application;
using ShabbatBoard.Domain;
using System.Globalization;

namespace ShabbatBoard.Infrastructure;

public static class CalendarProjector
{
    public static IReadOnlyList<Shabbat> ProjectCycle(int year, HebcalResponse response)
    {
        CalendarRules.ValidateYear(year);
        if (response.Items is not { Count: > 0 } || response.Items.Any(item => item is null))
            throw Incomplete();
        var boundaries = response.Items
            .Where(item => item.Category == "parashat" && item.Hebrew == "פרשת בראשית")
            .Select(item => ParseDate(item.Date)).ToArray();
        var first = Boundary(year);
        var next = Boundary(year + 1);
        var dates = new List<DateOnly>();
        for (var date = first; date < next; date = date.AddDays(7))
            dates.Add(date);
        return ProjectDates(dates, response);

        DateOnly Boundary(int boundaryYear)
        {
            var matches = boundaries.Where(date => date.Year == boundaryYear).ToArray();
            if (matches.Length != 1 || matches[0].DayOfWeek != DayOfWeek.Saturday)
                throw new UpstreamException("Hebcal returned missing or invalid Bereshit cycle boundaries.");
            return matches[0];
        }
    }

    public static IReadOnlyList<Shabbat> Project(int year, HebcalResponse response) =>
        ProjectDates(CalendarRules.Saturdays(year), response);

    private static IReadOnlyList<Shabbat> ProjectDates(IReadOnlyList<DateOnly> dates, HebcalResponse response)
    {
        if (response.Items is not { Count: > 0 })
            throw Incomplete();

        var events = response.Items.Select(item =>
        {
            if (item is null)
                throw Incomplete();
            return (Date: ParseDate(item.Date), Event: item);
        }).ToArray();
        var byDate = events.ToLookup(item => item.Date, item => item.Event);
        var roshChodesh = events.Where(e => e.Event.Category == "roshchodesh")
            .OrderBy(e => e.Date).ToArray();
        var rows = new List<Shabbat>();

        foreach (var date in dates)
        {
            var daily = byDate[date].ToArray();
            var hebrew = UniqueEvent(daily, "hebdate")?.DateParts;
            if (string.IsNullOrWhiteSpace(hebrew?.D) || string.IsNullOrWhiteSpace(hebrew.M)
                || string.IsNullOrWhiteSpace(hebrew.Y))
                throw Incomplete();

            var parasha = UniqueEvent(daily, "parashat");
            var holidays = daily.Where(e => e.Category == "holiday").ToArray();
            var festival = holidays.Any(e => e.Yomtov) || (parasha is null && holidays.Length > 0);
            var reading = festival
                ? string.Join(" / ", holidays.Where(e => e.Yomtov || parasha is null).Select(e => e.Hebrew).Distinct())
                : parasha?.Hebrew;
            if (string.IsNullOrWhiteSpace(reading))
                throw Incomplete();

            var mevarchim = UniqueEvent(daily, "mevarchim");
            string? month = null;
            var roshDates = new List<DateOnly>();
            if (mevarchim is not null)
            {
                month = Month(mevarchim.Hebrew, "מברכים חודש ");
                var candidates = roshChodesh.Where(e => e.Date > date && e.Date <= date.AddDays(7)).ToArray();
                if (candidates.Length == 0)
                    throw Incomplete();
                var first = candidates[0];
                if (Month(first.Event.Hebrew, "ראש חודש ") != month || month == "תשרי")
                    throw Incomplete();
                roshDates.Add(first.Date);
                var second = roshChodesh.FirstOrDefault(e => e.Date == first.Date.AddDays(1)
                    && e.Event.Hebrew == first.Event.Hebrew);
                if (second.Event is not null)
                    roshDates.Add(second.Date);
            }
            rows.Add(new(date, $"{hebrew.D} {hebrew.M} {hebrew.Y}", reading, festival,
                mevarchim is not null, month, roshDates));
        }

        // Validate source completeness against the first day of each Rosh Chodesh.
        var includedDates = dates.ToHashSet();
        foreach (var first in roshChodesh.Where((item, index) => index == 0
            || roshChodesh[index - 1].Date != item.Date.AddDays(-1)
            || roshChodesh[index - 1].Event.Hebrew != item.Event.Hebrew))
        {
            var month = Month(first.Event.Hebrew, "ראש חודש ");
            if (month == "תשרי")
                continue;
            var distance = ((int)first.Date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
            var blessingDate = first.Date.AddDays(-(distance == 0 ? 7 : distance));
            if (includedDates.Contains(blessingDate)
                && !rows.Any(row => row.Date == blessingDate && row.BlessedMonth == month))
                throw Incomplete();
        }

        return rows;
    }

    private static DateOnly ParseDate(string? value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date))
            throw Incomplete();
        return date;
    }

    private static string Month(string? title, string prefix)
    {
        if (title is null || !title.StartsWith(prefix, StringComparison.Ordinal)
            || title.Length == prefix.Length)
            throw Incomplete();
        return title[prefix.Length..];
    }

    private static HebcalEvent? UniqueEvent(HebcalEvent[] events, string category)
    {
        var matches = events.Where(e => e.Category == category).Take(2).ToArray();
        if (matches.Length > 1)
            throw Incomplete();
        return matches.FirstOrDefault();
    }

    private static UpstreamException Incomplete() =>
        new("Hebcal returned incomplete or inconsistent calendar data.");
}
