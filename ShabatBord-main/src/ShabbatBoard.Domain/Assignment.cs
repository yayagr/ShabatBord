namespace ShabbatBoard.Domain;

public sealed record Assignment(DateOnly Date, string FamilyName, Guid Version)
{
    public static Assignment Create(DateOnly date, string? familyName)
    {
        CalendarRules.ValidateDate(date);
        var name = familyName?.Trim().Normalize();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsControl))
            throw new RuleException("יש להזין שם משפחה באורך 1–100 תווים, ללא תווי בקרה.");
        return new(date, name, Guid.NewGuid());
    }
}

public enum RuleFailure { Validation, NotFound, Conflict }

public sealed class RuleException(string message, RuleFailure failure = RuleFailure.Validation) : Exception(message)
{
    public RuleFailure Failure { get; } = failure;
}

public static class CalendarRules
{
    public const int MinYear = 1900;
    public const int MaxYear = 2100;
    public const int MaxAssignmentYear = MaxYear + 1;

    public static void ValidateYear(int year)
    {
        if (year is < MinYear or > MaxYear)
            throw new RuleException($"יש לבחור שנה לועזית בין {MinYear} ל־{MaxYear}.");
    }

    public static void ValidateDate(DateOnly date)
    {
        if (date.Year is < MinYear or > MaxAssignmentYear)
            throw new RuleException($"תאריך השיבוץ חייב להיות בין השנים {MinYear} ו־{MaxAssignmentYear}.");
        if (date.DayOfWeek != DayOfWeek.Saturday)
            throw new RuleException("אפשר לשבץ משפחה רק לתאריך שחל בשבת.");
    }

    public static IReadOnlyList<DateOnly> Saturdays(int year)
    {
        ValidateYear(year);
        var first = new DateOnly(year, 1, 1);
        first = first.AddDays(((int)DayOfWeek.Saturday - (int)first.DayOfWeek + 7) % 7);
        var dates = new List<DateOnly>();
        for (var date = first; date.Year == year; date = date.AddDays(7))
            dates.Add(date);
        return dates;
    }
}
