using System.Text.Json;
using ShabbatBoard.Application;
using ShabbatBoard.Infrastructure;

namespace ShabbatBoard.Tests;

public class CalendarTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<HebcalResponse> Fixture(int year)
    {
        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"israel-{year}.json"));
        return (await JsonSerializer.DeserializeAsync<HebcalResponse>(file, JsonOptions))!;
    }

    [Theory]
    [InlineData(2022, 53)]
    [InlineData(2027, 52)]
    [InlineData(2028, 53)]
    public async Task IncludesAllShabbatotWithHebrewDatesAndReadings(int year, int expected)
    {
        var rows = CalendarProjector.Project(year, await Fixture(year));
        Assert.Equal(expected, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(year, row.Date.Year);
            Assert.Equal(DayOfWeek.Saturday, row.Date.DayOfWeek);
            Assert.False(string.IsNullOrWhiteSpace(row.HebrewDate));
            Assert.False(string.IsNullOrWhiteSpace(row.Reading));
        });
    }

    [Fact]
    public async Task SaturdayRoshChodeshIsBlessedOneWeekEarlier()
    {
        var rows = CalendarProjector.Project(2027, await Fixture(2027));
        var blessing = rows.Single(r => r.Date == new DateOnly(2027, 1, 2));
        Assert.True(blessing.IsMevarchim);
        Assert.Equal("שבט", blessing.BlessedMonth);
        Assert.Equal(new DateOnly(2027, 1, 9), Assert.Single(blessing.RoshChodeshDates));
        Assert.False(rows.Single(r => r.Date == new DateOnly(2027, 1, 9)).IsMevarchim);
    }

    [Fact]
    public async Task GroupsTwoDayRoshChodeshAndDistinguishesLeapAdars()
    {
        var rows = CalendarProjector.Project(2027, await Fixture(2027));
        Assert.Contains(rows, r => r.IsMevarchim && r.BlessedMonth == "אדר א׳");
        Assert.Contains(rows, r => r.IsMevarchim && r.BlessedMonth == "אדר ב׳");
        Assert.Contains(rows, r => r.RoshChodeshDates.Count == 2);
        foreach (var row in rows.Where(r => r.IsMevarchim))
        {
            Assert.InRange(row.RoshChodeshDates[0].DayNumber - row.Date.DayNumber, 1, 7);
            if (row.RoshChodeshDates.Count == 2)
                Assert.Equal(row.RoshChodeshDates[0].AddDays(1), row.RoshChodeshDates[1]);
            Assert.NotEqual("תשרי", row.BlessedMonth);
        }
    }

    [Fact]
    public async Task IncludesRoshChodeshAcrossGregorianBoundary()
    {
        var rows = CalendarProjector.Project(2024, await Fixture(2024));
        var last = rows.Single(r => r.Date == new DateOnly(2024, 12, 28));
        Assert.True(last.IsMevarchim);
        Assert.Equal(new[] { new DateOnly(2024, 12, 31), new DateOnly(2025, 1, 1) }, last.RoshChodeshDates);
    }

    [Fact]
    public async Task UsesIsraelReadingAndFestivalInsteadOfFabricatedParasha()
    {
        var rows = CalendarProjector.Project(2022, await Fixture(2022));
        Assert.Equal("פרשת אחרי מות", rows.Single(r => r.Date == new DateOnly(2022, 4, 23)).Reading);
        var pesach = rows.Single(r => r.Date == new DateOnly(2022, 4, 16));
        Assert.True(pesach.IsFestival);
        Assert.Contains("פסח", pesach.Reading);
    }

    [Fact]
    public void RejectsMissingUpstreamItems() =>
        Assert.Throws<UpstreamException>(() => CalendarProjector.Project(2027, new()));

    [Fact]
    public async Task RejectsMissingHebrewDate()
    {
        var fixture = await Fixture(2027);
        fixture.Items!.RemoveAll(e => e.Date == "2027-01-02" && e.Category == "hebdate");
        Assert.Throws<UpstreamException>(() => CalendarProjector.Project(2027, fixture));
    }

    [Fact]
    public async Task RejectsMissingReading()
    {
        var fixture = await Fixture(2027);
        fixture.Items!.RemoveAll(e => e.Date == "2027-01-02" && e.Category == "parashat");
        Assert.Throws<UpstreamException>(() => CalendarProjector.Project(2027, fixture));
    }

    [Fact]
    public async Task RejectsMissingMevarchimInsteadOfSilentlyShowingOrdinaryShabbat()
    {
        var fixture = await Fixture(2027);
        fixture.Items!.RemoveAll(e => e.Date == "2027-01-02" && e.Category == "mevarchim");
        Assert.Throws<UpstreamException>(() => CalendarProjector.Project(2027, fixture));
    }

    [Fact]
    public async Task DuplicateHebrewDatesAreAnUpstreamFailure()
    {
        var fixture = await Fixture(2027);
        fixture.Items!.Add(fixture.Items.First(e => e.Date == "2027-01-02" && e.Category == "hebdate"));
        Assert.Throws<UpstreamException>(() => CalendarProjector.Project(2027, fixture));
    }
}
