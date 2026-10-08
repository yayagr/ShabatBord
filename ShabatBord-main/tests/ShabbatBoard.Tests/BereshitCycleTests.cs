using System.Text.Json;
using ShabbatBoard.Application;
using ShabbatBoard.Domain;
using ShabbatBoard.Infrastructure;

namespace ShabbatBoard.Tests;

public class BereshitCycleTests
{
    public static async Task<HebcalResponse> Fixture(int year)
    {
        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", $"israel-cycle-{year}.json"));
        return (await JsonSerializer.DeserializeAsync<HebcalResponse>(file,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    [Theory]
    [InlineData(2026, "2026-10-10", "2027-10-30", 55)]
    [InlineData(2027, "2027-10-30", "2028-10-14", 50)]
    [InlineData(2100, "2100-10-30", "2101-10-22", 51)]
    public async Task StartsAtBereshitAndIncludesEverySaturdayUntilNextBereshit(
        int year, string first, string next, int expected)
    {
        var rows = CalendarProjector.ProjectCycle(year, await Fixture(year));
        Assert.Equal(expected, rows.Count);
        Assert.Equal(DateOnly.Parse(first), rows[0].Date);
        Assert.Equal("פרשת בראשית", rows[0].Reading);
        Assert.Equal(DateOnly.Parse(next).AddDays(-7), rows[^1].Date);
        Assert.Single(rows, row => row.Reading == "פרשת בראשית");
        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.Equal(pair.First.Date.AddDays(7), pair.Second.Date));
        Assert.All(rows, row =>
        {
            Assert.Equal(DayOfWeek.Saturday, row.Date.DayOfWeek);
            Assert.False(string.IsNullOrWhiteSpace(row.HebrewDate));
            Assert.False(string.IsNullOrWhiteSpace(row.Reading));
        });
    }

    [Theory]
    [InlineData(2027)]
    [InlineData(2028)]
    public async Task RejectsMissingBoundary(int removedYear)
    {
        var fixture = await Fixture(2027);
        fixture.Items!.RemoveAll(e => e.Category == "parashat" && e.Hebrew == "פרשת בראשית"
            && e.Date!.StartsWith($"{removedYear}-", StringComparison.Ordinal));
        Assert.Throws<UpstreamException>(() => CalendarProjector.ProjectCycle(2027, fixture));
    }

    [Fact]
    public async Task RejectsDuplicateBoundary()
    {
        var fixture = await Fixture(2027);
        fixture.Items!.Add(fixture.Items.First(e => e.Category == "parashat" && e.Hebrew == "פרשת בראשית"));
        Assert.Throws<UpstreamException>(() => CalendarProjector.ProjectCycle(2027, fixture));
    }

    [Fact]
    public async Task RejectsBoundaryNotOnSaturday()
    {
        var fixture = await Fixture(2027);
        var index = fixture.Items!.FindIndex(e => e.Category == "parashat" && e.Hebrew == "פרשת בראשית");
        fixture.Items[index] = fixture.Items[index] with { Date = "2027-10-31" };
        Assert.Throws<UpstreamException>(() => CalendarProjector.ProjectCycle(2027, fixture));
    }

    [Fact]
    public void RejectsNullUpstreamEvents()
    {
        var fixture = JsonSerializer.Deserialize<HebcalResponse>("""{"items":[null]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Throws<UpstreamException>(() => CalendarProjector.ProjectCycle(2027, fixture));
    }

    [Fact]
    public async Task RejectsMissingDateInFollowingGregorianYear()
    {
        var fixture = await Fixture(2027);
        fixture.Items!.RemoveAll(e => e.Date == "2028-01-01" && e.Category == "hebdate");
        Assert.Throws<UpstreamException>(() => CalendarProjector.ProjectCycle(2027, fixture));
    }

    [Fact]
    public async Task LeapCycleContainsBothAdarsAndExcludesTishreiBlessing()
    {
        var rows = CalendarProjector.ProjectCycle(2026, await Fixture(2026));
        Assert.Contains(rows, row => row.BlessedMonth == "אדר א׳");
        Assert.Contains(rows, row => row.BlessedMonth == "אדר ב׳");
        Assert.DoesNotContain(rows, row => row.BlessedMonth == "תשרי");
    }

    [Fact]
    public async Task UpperBoundCycleRemainsFullyAssignable()
    {
        var rows = CalendarProjector.ProjectCycle(2100, await Fixture(2100));
        Assert.Equal(2101, rows[^1].Date.Year);
        Assert.All(rows, row => Assert.Equal(row.Date, Assignment.Create(row.Date, "Cohen").Date));
    }
}
