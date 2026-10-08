using ShabbatBoard.Domain;

namespace ShabbatBoard.Application;

public sealed record Shabbat(
    DateOnly Date,
    string HebrewDate,
    string Reading,
    bool IsFestival,
    bool IsMevarchim,
    string? BlessedMonth,
    IReadOnlyList<DateOnly> RoshChodeshDates,
    Assignment? Assignment = null);

// Year identifies the Gregorian year in which this Bereshit-to-Bereshit cycle starts.
public sealed record CalendarYear(int Year, IReadOnlyList<Shabbat> Shabbatot);

public interface ICalendarProvider
{
    Task<IReadOnlyList<Shabbat>> GetYearAsync(int year, CancellationToken cancellationToken);
}

public interface IAssignmentStore
{
    Task<IReadOnlyList<Assignment>> ReadAsync(CancellationToken cancellationToken);
    Task<Assignment> ClaimAsync(DateOnly date, string? name, CancellationToken cancellationToken);
    Task<Assignment> EditAsync(DateOnly date, string? name, Guid version, CancellationToken cancellationToken);
    Task RemoveAsync(DateOnly date, Guid version, CancellationToken cancellationToken);
}

public sealed class UpstreamException(string message, Exception? inner = null) : Exception(message, inner);
