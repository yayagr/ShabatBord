using ShabbatBoard.Domain;

namespace ShabbatBoard.Tests;

public class DomainTests
{
    [Theory]
    [InlineData(2027, 52)]
    [InlineData(2028, 53)]
    public void EnumeratesEverySaturday(int year, int count)
    {
        var dates = CalendarRules.Saturdays(year);
        Assert.Equal(count, dates.Count);
        Assert.All(dates, date =>
        {
            Assert.Equal(year, date.Year);
            Assert.Equal(DayOfWeek.Saturday, date.DayOfWeek);
        });
        Assert.Equal(dates.Count, dates.Distinct().Count());
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(2101)]
    public void RejectsUnsupportedYear(int year) =>
        Assert.Throws<RuleException>(() => CalendarRules.Saturdays(year));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Cohen\nLevi")]
    public void RejectsInvalidName(string? name) =>
        Assert.Throws<RuleException>(() => Assignment.Create(new(2027, 1, 2), name));

    [Fact]
    public void RejectsLongName() =>
        Assert.Throws<RuleException>(() => Assignment.Create(new(2027, 1, 2), new string('a', 101)));

    [Fact]
    public void RejectsNonSaturday() =>
        Assert.Throws<RuleException>(() => Assignment.Create(new(2027, 1, 3), "Cohen"));

    [Fact]
    public void TrimsNamesAndCreatesVersion()
    {
        var assignment = Assignment.Create(new(2027, 1, 2), "  משפחת כהן  ");
        Assert.Equal("משפחת כהן", assignment.FamilyName);
        Assert.NotEqual(Guid.Empty, assignment.Version);
    }

    [Fact]
    public void AllowsNextYearAssignmentsForLastSupportedCycle()
    {
        Assert.Equal(new DateOnly(2101, 10, 15), Assignment.Create(new(2101, 10, 15), "Cohen").Date);
        Assert.Throws<RuleException>(() => CalendarRules.ValidateYear(2101));
        Assert.Throws<RuleException>(() => Assignment.Create(new(2102, 1, 7), "Cohen"));
    }
}
